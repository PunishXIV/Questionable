using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Application.Network.WorkDefinitions;
using FFXIVClientStructs.FFXIV.Client.Game;
using Microsoft.Extensions.Logging;
using Questionable.Data;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.PathData;
using Questionable.QuestPaths;
using Questionable.Validation;
using Questionable.Validation.Validators;
namespace Questionable.Controller;

internal sealed class QuestRegistry
{
    private readonly IChatGui _chatGui;
    private readonly Dictionary<uint, (ElementId QuestId, QuestStep Step)> _contentFinderConditionIds = [];
    private readonly IDataManager _dataManager;
    private readonly JsonSchemaValidator _jsonSchemaValidator;
    private readonly ILogger<QuestRegistry> _logger;
    private readonly List<(uint ContentFinderConditionId, ElementId QuestId, int Sequence)> _lowPriorityContentFinderConditionQuests = [];
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly QuestData _questData;
    private readonly Dictionary<ElementId, Quest> _quests = [];
    private readonly QuestValidator _questValidator;

    private readonly ICallGateProvider<object> _reloadDataIpc;
    private readonly TerritoryData _territoryData;

    public QuestRegistry(
        IDalamudPluginInterface pluginInterface,
        QuestData questData,
        QuestValidator questValidator,
        JsonSchemaValidator jsonSchemaValidator,
        ILogger<QuestRegistry> logger,
        TerritoryData territoryData,
        IDataManager dataManager,
        IChatGui chatGui)
    {
        _pluginInterface = pluginInterface;
        _questData = questData;
        _questValidator = questValidator;
        _jsonSchemaValidator = jsonSchemaValidator;
        _logger = logger;
        _territoryData = territoryData;
        _chatGui = chatGui;
        _dataManager = dataManager;
        _reloadDataIpc = _pluginInterface.GetIpcProvider<object>("Questionable.ReloadData");
    }

    public IEnumerable<Quest> AllQuests => _quests.Values;
    public int Count => _quests.Count(x => !x.Value.Root.Disabled);
    public int ValidationIssueCount => _questValidator.IssueCount;
    public int ValidationErrorCount => _questValidator.ErrorCount;

    public IReadOnlyList<(uint ContentFinderConditionId, ElementId QuestId, int Sequence)>
        LowPriorityContentFinderConditionQuests => _lowPriorityContentFinderConditionQuests;

    public event EventHandler? Reloaded;

    public void Reload()
    {
        _questValidator.Reset();
        _quests.Clear();
        _contentFinderConditionIds.Clear();
        _lowPriorityContentFinderConditionQuests.Clear();

        LoadQuestsFromAssembly();
        LoadQuestsFromDownloadedBundle();
        LoadQuestsFromProjectDirectory();

        try
        {
            LoadFromDirectory(new(Path.Combine(_pluginInterface.ConfigDirectory.FullName, "Quests")),
                Quest.ESource.UserDirectory);
        }
        catch (Exception e)
        {
            _logger.LogError(e,
                "Failed to load all quests from user directory (some may have been successfully loaded)");
        }

        LoadCfcIds();
        ValidateQuests();
        Reloaded?.Invoke(this, EventArgs.Empty);
        try
        {
            _reloadDataIpc.SendMessage();
        }
        catch (Exception e)
        {
            // why does this even throw
            _logger.LogWarning(e, "Error during Reload.SendMessage IPC");
        }

        _logger.LogInformation("Loaded {Count} quests in total", _quests.Count);
    }

    [Conditional("RELEASE")]
    private void LoadQuestsFromAssembly()
    {
        _logger.LogInformation("Loading quests from assembly");

        foreach ((ElementId questId, QuestRoot questRoot) in AssemblyQuestLoader.Quests)
        {
            try
            {
                IQuestInfo questInfo = _questData.GetQuestInfo(questId);
                Quest quest = new()
                {
                    Id = questId,
                    Root = questRoot,
                    Info = questInfo,
                    Source = Quest.ESource.Assembly
                };
                _quests[quest.Id] = quest;
            }
            catch (Exception e)
            {
                _logger.LogWarning("Not loading unknown quest {QuestId} from assembly: {Message}", questId, e.Message);
            }
        }

        _logger.LogInformation("Loaded {Count} quests from assembly", _quests.Count);
    }

    [Conditional("DEBUG")]
    private void LoadQuestsFromProjectDirectory()
    {
        DirectoryInfo? solutionDirectory = _pluginInterface.AssemblyLocation.Directory?.Parent?.Parent;
        if (solutionDirectory != null)
        {
            DirectoryInfo pathProjectDirectory = new(Path.Combine(solutionDirectory.FullName, "QuestPaths"));
            if (pathProjectDirectory.Exists)
            {
                try
                {
                    foreach (string expansionFolder in ExpansionData.ExpansionFolders.Values)
                    {
                        LoadFromDirectory(
                            new(Path.Combine(pathProjectDirectory.FullName, expansionFolder)),
                            Quest.ESource.ProjectDirectory,
                            LogLevel.Trace);
                    }
                }
                catch (Exception e)
                {
                    _quests.Clear();

                    _chatGui.PrintError($"Unable to load quests - {e.GetType().Name}: {e.Message}", CommandHandler.MessageTag, CommandHandler.TagColor);
                    _logger.LogError(e, "Failed to load quests from project directory");
                }
            }
        }
    }

    /// <summary>
    ///     Loads quests from a downloaded path bundle (<c>{ConfigDirectory}/PathData/bundle.zip</c>)
    ///     if one is present. Entries here override the compiled baseline but are themselves
    ///     overridden by the hand-authored user directory. A single bad entry is skipped rather
    ///     than aborting the rest of the bundle.
    /// </summary>
    private void LoadQuestsFromDownloadedBundle()
    {
        string bundlePath = PathDataBundle.GetBundlePath(_pluginInterface);
        if (!File.Exists(bundlePath))
            return;

        try
        {
            using ZipArchive archive = ZipFile.OpenRead(bundlePath);
            PathDataManifest? manifest = PathDataBundle.ReadManifest(archive);
            if (manifest == null)
            {
                _logger.LogWarning("Downloaded path bundle has no manifest; ignoring it");
                return;
            }

            // Gate A: never load a bundle that needs a newer plugin than this one.
            if (!manifest.IsCompatibleWith(PathDataFormat.CurrentVersion))
            {
                _logger.LogWarning(
                    "Ignoring downloaded path bundle (data version {DataVersion}): it requires plugin data format {MinFormat}, this plugin supports {CurrentFormat}",
                    manifest.DataVersion, manifest.MinPluginDataFormat, PathDataFormat.CurrentVersion);
                return;
            }

            int loaded = 0, failed = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith(PathDataBundle.QuestPathPrefix, StringComparison.Ordinal) ||
                    !entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    using Stream stream = entry.Open();
                    LoadQuestFromStream(entry.Name, stream, Quest.ESource.DownloadedBundle);
                    ++loaded;
                }
                catch (Exception e)
                {
                    ++failed;
                    _logger.LogWarning(e, "Failed to load quest '{Entry}' from downloaded bundle (skipped)",
                        entry.FullName);
                }
            }

            _logger.LogInformation("Loaded {Loaded} quests from downloaded path bundle (data version {DataVersion}){Failed}",
                loaded, manifest.DataVersion, failed > 0 ? $", {failed} skipped" : string.Empty);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to load downloaded path bundle; falling back to the compiled baseline");
        }
    }

    private void LoadCfcIds()
    {
        foreach (Quest quest in _quests.Values)
        {
            foreach (QuestSequence dutySequence in quest.AllSequences())
            {
                foreach (QuestStep dutyStep in dutySequence.Steps.Where(x =>
                    x.InteractionType is EInteractionType.Duty or EInteractionType.SinglePlayerDuty))
                {
                    if (dutyStep is { InteractionType: EInteractionType.Duty, DutyOptions: { } dutyOptions })
                    {
                        _contentFinderConditionIds[dutyOptions.ContentFinderConditionId] = (quest.Id, dutyStep);
                        if (dutyOptions.LowPriority)
                        {
                            _lowPriorityContentFinderConditionQuests.Add((dutyOptions.ContentFinderConditionId,
                                quest.Id, dutySequence.Sequence));
                        }
                    }
                    else if (dutyStep.InteractionType == EInteractionType.SinglePlayerDuty &&
                             _territoryData.TryGetContentFinderConditionForSoloInstance(quest.Id,
                                 dutyStep.SinglePlayerDutyIndex, out TerritoryData.ContentFinderConditionData? cfcData))
                    {
                        _contentFinderConditionIds[cfcData.ContentFinderConditionId] = (quest.Id, dutyStep);
                    }
                }
            }
        }
    }

    // The compiled baseline and downloaded bundles are validated by CI before publication, so
    // only the hand-authored user directory (and dev project directory) is validated at runtime.
    private void ValidateQuests() => _questValidator.Validate(_quests.Values
        .Where(x => x.Source is not (Quest.ESource.Assembly or Quest.ESource.DownloadedBundle))
        .ToList());

    private void LoadQuestFromStream(string fileName, Stream stream, Quest.ESource source)
    {
        if (source == Quest.ESource.UserDirectory)
            _logger.LogTrace("Loading quest from '{FileName}'", fileName);
        ElementId? questId = ExtractQuestIdFromName(fileName);
        if (questId == null)
            return;

        JsonNode questNode = JsonNode.Parse(stream)!;

        // Downloaded bundles are trusted (CI-validated + checksum-verified); only runtime-loaded
        // hand-authored data is schema-validated here.
        if (source != Quest.ESource.DownloadedBundle)
            _jsonSchemaValidator.Enqueue(questId, questNode);

        QuestRoot questRoot = questNode.Deserialize<QuestRoot>()!;
        IQuestInfo questInfo = _questData.GetQuestInfo(questId);
        Quest quest = new()
        {
            Id = questId,
            Root = questRoot,
            Info = questInfo,
            Source = source
        };
        _quests[quest.Id] = quest;
    }

    private void LoadFromDirectory(DirectoryInfo directory, Quest.ESource source,
        LogLevel logLevel = LogLevel.Information)
    {
        if (!directory.Exists)
        {
            _logger.LogInformation("Not loading quests from {DirectoryName} (doesn't exist)", directory);
            return;
        }

        if (source == Quest.ESource.UserDirectory)
            _logger.Log(logLevel, "Loading quests from {DirectoryName}", directory);
        foreach (FileInfo fileInfo in directory.GetFiles("*.json"))
        {
            try
            {
                using FileStream stream = new(fileInfo.FullName, FileMode.Open, FileAccess.Read);
                LoadQuestFromStream(fileInfo.Name, stream, source);
            }
            catch (Exception e)
            {
                throw new InvalidDataException($"Unable to load file {fileInfo.FullName}", e);
            }
        }

        foreach (DirectoryInfo childDirectory in directory.GetDirectories())
            LoadFromDirectory(childDirectory, source, logLevel);
    }

    private static ElementId? ExtractQuestIdFromName(string resourceName)
    {
        string name = resourceName.Substring(0, resourceName.Length - ".json".Length);
        name = name.Substring(name.LastIndexOf('.') + 1);

        if (!name.Contains('_', StringComparison.Ordinal))
            return null;

        string[] parts = name.Split('_', 2);
        return ElementId.FromString(parts[0]);
    }

    public bool IsKnownQuest(ElementId questId) => _quests.ContainsKey(questId);

    public bool TryGetQuest(ElementId questId, [NotNullWhen(true)] out Quest? quest) => _quests.TryGetValue(questId, out quest);

    public List<QuestInfo> GetKnownClassJobQuests(Job classJob, bool includeRoleQuests = true)
    {
        List<QuestInfo> allQuests = [.. _questData.GetClassJobQuests(classJob, includeRoleQuests)];
        if (classJob.AsJob() != classJob)
            allQuests.AddRange(_questData.GetClassJobQuests(classJob.AsJob(), includeRoleQuests));

        return allQuests
            .Where(x => IsKnownQuest(x.QuestId))
            .ToList();
    }

    public bool TryGetDutyByContentFinderConditionId(uint cfcId, [NotNullWhen(true)] out DutyOptions? dutyOptions)
    {
        if (_contentFinderConditionIds.TryGetValue(cfcId, out (ElementId QuestId, QuestStep Step) value))
        {
            dutyOptions = value.Step.DutyOptions;
            return dutyOptions != null;
        }

        dutyOptions = null;
        return false;
    }

    internal static FileInfo AssemblyLocation => Svc.PluginInterface.AssemblyLocation;
    public static string GetFilename(IQuestInfo info) => $"{info.QuestId}_{info.SimplifiedName}.json";
#if DEBUG
    public static (bool, string) CreatePath(IQuestInfo info)
    {
        DirectoryInfo? targetFolder = new(Path.Combine(AssemblyLocation.Directory!.Parent!.Parent!.FullName, "QuestPaths"));
        if (targetFolder == null)
            return (false, "couldn't find QuestPaths folder");
        // TODO move quest paths to directories matching JournalCategory/JournalGenre so paths can be created automatically
        return (false, "Not implemented");
    }
    public static (bool, string) OpenEditor(IQuestInfo info)
    {
        return OpenEditor(GetFilename(info));
    }
    public (bool, string) OpenEditor(ushort questId)
    {
        if (TryGetQuest(new QuestId(questId), out Quest? quest))
            return OpenEditor(GetFilename(quest.Info));
        return (false, $"could not get quest from {questId}");
    }
    public unsafe (bool, string) OpenEditor()
    {
        _logger.LogDebug("OpenEditor trackedQuests");
        QuestManager* questManager = QuestManager.Instance();
        ushort? questId = null;
        if (questManager != null)
        {
            for (int i = questManager->TrackedQuests.Length - 1; i >= 0; --i)
            {
                TrackingWork trackedQuest = questManager->TrackedQuests[i];
                switch (trackedQuest.QuestType)
                {
                    case 1:
                        questId = questManager->NormalQuests[trackedQuest.Index].QuestId;
                        break;
                    case 2:
                        break;
                }

                if (questId != null)
                    break;
            }
        }

        if (questId != null)
            return OpenEditor(questId.Value);
        return (false, "could not get tracked quest");
    }

    public static (bool, string) OpenEditor(string filename)
    {
        DirectoryInfo? targetFolder = new(Path.Combine(AssemblyLocation.Directory!.Parent!.Parent!.FullName, "QuestPaths"));
        if (targetFolder == null)
            return (false, "couldn't find QuestPaths folder");
        FileInfo? file = FindFilenameInDirectory(targetFolder, filename);
        if (file == null)
            return (false, $"couldn't find {filename}");
        Process.Start(new ProcessStartInfo
        {
            FileName = filename,
            WorkingDirectory = file.DirectoryName,
            UseShellExecute = true
        });
        return (true, file.FullName);
    }

    public static FileInfo? FindFilenameInDirectory(DirectoryInfo root, string filename)
    {
        foreach (FileInfo file in root.GetFiles())
        {
            if (file.Name.Equals(filename, StringComparison.OrdinalIgnoreCase) || // if filename match case insensitive
                file.Name.StartsWith( filename[..(filename.IndexOf('_')+1)] )) // if ID at start of filename match
                return file;
        }

        foreach (DirectoryInfo directory in root.GetDirectories())
        {
            if (FindFilenameInDirectory(directory, filename) is FileInfo result)
                return result;
        }

        return null;
    }
#endif
}
