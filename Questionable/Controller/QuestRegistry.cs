using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dalamud.Plugin.Ipc;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Application.Network.WorkDefinitions;
using FFXIVClientStructs.FFXIV.Client.Game;
using Questionable.Model.Questing;
using Questionable.QuestPaths;
using static Questionable.Domain.QuestInfo;
using static Questionable.Utils.CacheUtils;
using Sheets = Lumina.Excel.Sheets;
namespace Questionable.Controller;

// TODO: refactor — heavy nesting (29 lines indented ≥6 levels, max indent ~8 levels).
[RegisterSingleton]
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
    private readonly Configuration _configuration;

    private readonly ICallGateProvider<object> _reloadDataIpc;
    private readonly TerritoryData _territoryData;

    public QuestRegistry(
        IDalamudPluginInterface pluginInterface,
        QuestData questData,
        QuestValidator questValidator,
        JsonSchemaValidator jsonSchemaValidator,
        ILogger<QuestRegistry> logger,
        TerritoryData territoryData,
        Configuration configuration,
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
        _configuration = configuration;
        _reloadDataIpc = _pluginInterface.GetIpcProvider<object>("Questionable.ReloadData");
    }

    public IEnumerable<Quest> AllQuests => _quests.Values;
    private CachedValue<int> _count = new(ttlSeconds: 1);
    public int Count => _count.Get(() => _quests.Count(x => !x.Value.Root.Disabled));
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

        if (!LoadQuestsFromDownloadedBundle())
            //LoadQuestsFromAssembly();
            _logger.LogWarning("Bundled quests were not loaded, we have no quests!");
        if (_configuration.Advanced.Debug || Svc.PluginInterface.IsDev
#if DEBUG
        || true
#endif
        )
        {
            LoadQuestsFromProjectDirectory();

            try
            {
                var _ = LoadFromDirectory(new(Path.Combine(_pluginInterface.ConfigDirectory.FullName, "Quests")),
                    Quest.ESource.UserDirectory);
            }
            catch (Exception e)
            {
                _logger.LogError(e,
                    "Failed to load all quests from user directory (some may have been successfully loaded)");
            }
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
                    uint count = 0;
                    foreach (string expansionFolder in ExpansionData.ExpansionFolders.Values)
                    {
                        count += LoadFromDirectory(
                            new(Path.Combine(pathProjectDirectory.FullName, expansionFolder)),
                            Quest.ESource.ProjectDirectory,
                            LogLevel.Trace);
                    }
                    _logger.LogInformation("Loaded {Count} quests from project directory", count);
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
    private bool LoadQuestsFromDownloadedBundle()
    {
        string bundlePath = PathDataBundle.GetBundlePath(_pluginInterface);
        if (!File.Exists(bundlePath))
            return false;

        try
        {
            using ZipArchive archive = ZipFile.OpenRead(bundlePath);
            PathDataManifest? manifest = PathDataBundle.ReadManifest(archive);
            if (manifest == null)
            {
                _logger.LogWarning("Downloaded path bundle has no manifest; ignoring it");
                return false;
            }

            // Gate A: never load a bundle that needs a newer plugin than this one.
            if (!manifest.IsCompatibleWith(PathDataFormat.CurrentVersion))
            {
                _logger.LogWarning(
                    "Ignoring downloaded path bundle (data version {DataVersion}): it requires plugin data format {MinFormat}, this plugin supports {CurrentFormat}",
                    manifest.DataVersion, manifest.MinPluginDataFormat, PathDataFormat.CurrentVersion);
                return false;
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
            return false;
        }
        return true;
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

    private uint LoadFromDirectory(DirectoryInfo directory, Quest.ESource source,
        LogLevel logLevel = LogLevel.Information)
    {
        uint count = 0;
        if (!directory.Exists)
        {
            _logger.LogInformation("Not loading quests from {DirectoryName} (doesn't exist)", directory);
            return count;
        }

        if (source == Quest.ESource.UserDirectory || source == Quest.ESource.ProjectDirectory)
            _logger.Log(logLevel, "Loading quests from {DirectoryName}", directory);
        foreach (FileInfo fileInfo in directory.GetFiles("*.json"))
        {
            try
            {
                using FileStream stream = new(fileInfo.FullName, FileMode.Open, FileAccess.Read);
                LoadQuestFromStream(fileInfo.Name, stream, source);
                count += 1;
            }
            catch (Exception e)
            {
                throw new InvalidDataException($"Unable to load file {fileInfo.FullName}", e);
            }
        }

        foreach (DirectoryInfo childDirectory in directory.GetDirectories())
            count += LoadFromDirectory(childDirectory, source, logLevel);
        return count;
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

    public bool TryGetQuestByContentFinderConditionId(uint cfcId, out IQuestInfo? questInfo)
    {
        if (_contentFinderConditionIds.TryGetValue(cfcId, out (ElementId qId, QuestStep step) value) &&
                _questData.TryGetQuestInfo(value.qId, out questInfo))
            return questInfo != null;
        questInfo = null;
        return false;
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
    public static string GetFilename(IQuestInfo info) => GetFilename((QuestInfo)info);
    public static string GetFilename(QuestInfo info) => $"{info.QuestId}_{info.SimplifiedName}.json";
    public static QuestRoot CreateQuestRoot(QuestInfo info)
    {
        QuestSequence seq0 = new()
        {
            Sequence = 0,
            Steps = [
                    new QuestStep(
                        EInteractionType.AcceptQuest,
                        info.IssuerDataId,
                        info.IssuerLocation.Position,
                        info.IssuerLocation.Territory.RowId
                    ) {
                        Fly = GameFunctions.IsFlyingUnlocked(info.IssuerLocation.Territory.RowId) ? true : null
                    }
                ]
        };
        List<QuestSequence> sequences = [seq0];
        Svc.Log.Debug($"NumSequences: {info.NumSequences}");
        for (var i = 0; i <= info.NumSequences; i++)
        {
            SheetLevel? level = i < info.ToDoLocations.Count ? info.ToDoLocations[i] : null;
            if (level?.Position == null || i == 255)
                continue;
            sequences.Add(new QuestSequence
            {
                Sequence = (byte)(i + 1),
                Steps = [
                    new QuestStep(
                        level?.Object != null && level?.Object.RowId != 0 ? EInteractionType.Interact : EInteractionType.WalkTo,
                        level?.Object.RowId != 0 ? level?.Object.RowId : null,
                        level?.Position + new System.Numerics.Vector3(0,level?.Object.RowId == 0 ? 30 : 0,0),
                        level?.Territory.RowId ?? info.IssuerLocation.Territory.RowId
                    ) {
                        Fly = GameFunctions.IsFlyingUnlocked(level?.Territory.RowId ?? info.IssuerLocation.Territory.RowId) ? true : null
                    }
                ]
            });
        }
        QuestSequence seq255 = new()
        {
            Sequence = 255,
            Steps = [
                    new QuestStep(
                        EInteractionType.CompleteQuest,
                        info.ToDoLocations[^1].Object.RowId,
                        info.ToDoLocations[^1].Position,
                        info.ToDoLocations[^1].Territory.RowId
                    ) {
                        Fly = GameFunctions.IsFlyingUnlocked(info.ToDoLocations[^1].Territory.RowId) ? true : null
                    }
                ]
        };
        sequences.Add(seq255);
        var name = "Anonymous";
        var pluginConfig = Svc.PluginInterface.GetPluginConfig();
        if (pluginConfig is Configuration config)
            name = config.General.DisplayName;
        return new QuestRoot()
        {
            Author = [name],
            QuestSequence = sequences
        };
    }
    public static string GetQuestPathsDirectory()
    {
        var pluginConfig = Svc.PluginInterface.GetPluginConfig();
        if (pluginConfig is Configuration config && config.Advanced.Debug && Svc.PluginInterface.IsDev)
            return Path.Combine(AssemblyLocation.Directory!.Parent!.Parent!.FullName, "QuestPaths");
        return Path.Combine(Svc.PluginInterface.GetPluginConfigDirectory(), "Quests");
    }
    public static string? GetFullPath(IQuestInfo info, bool withFilename = true) => GetFullPath((QuestInfo)info, withFilename);
    public static string? GetFullPath(QuestInfo info, bool withFilename = true)
    {
        var filename = GetFilename(info);
        var pluginConfig = Svc.PluginInterface.GetPluginConfig();
        if (pluginConfig is Configuration config && config.Advanced.Debug && Svc.PluginInterface.IsDev)
        {
            DirectoryInfo? targetFolder = new(Path.Combine(AssemblyLocation.Directory!.Parent!.Parent!.FullName, "QuestPaths", ExpansionData.ExpansionFolders[info.Expansion]));
            if (targetFolder == null)
                return null;
            string? path = "";
            if (info.JournalGenre != null && info.JournalGenre != uint.MaxValue)
            {
                var genre = Svc.Data.GetExcelSheet<Sheets.JournalGenre>().GetRow(info.JournalGenre.Value);
                path = $"{genre.Name}";
                Svc.Log.Debug($"Genre: {genre.Name}");
                if (genre.JournalCategory.ValueNullable != null)
                {
                    var category = genre.JournalCategory.Value;
                    Svc.Log.Debug($"Category: {category.Name}");
                    if (category.Name != genre.Name)
                        path = Path.Combine($"{category.Name}", path);
                    if (category.JournalSection.ValueNullable != null)
                    {
                        var section = category.JournalSection.Value;
                        Svc.Log.Debug($"Section: {section.Name}");
                        if (section.Name != category.Name)
                        {
                            var catPath = $"{category.Name}".Replace($"{section.Name}", "").Trim();
                            path = Path.Combine($"{section.Name}", catPath, category.Name != genre.Name ? $"{genre.Name}" : "");
                        }
                    }
                }
            }
            if (path == null || path.Length == 0)
            {
                if (withFilename)
                    return Path.Combine(targetFolder.FullName, "Unsorted", filename);
                return Path.Combine(targetFolder.FullName, "Unsorted");
            }
            if (withFilename)
                return Path.Combine(targetFolder.FullName, path, filename);
            return Path.Combine(targetFolder.FullName, path);
        }
        if (withFilename)
            return Path.Combine(Svc.PluginInterface.GetPluginConfigDirectory(), "Quests", filename);
        return Path.Combine(Svc.PluginInterface.GetPluginConfigDirectory(), "Quests");
    }
    public static (bool, FileInfo?, string) CreatePath(IQuestInfo info) => CreatePath((QuestInfo)info);
    public static (bool, FileInfo?, string) CreatePath(QuestInfo info, Quest? quest = null, bool dryrun = false)
    {
        var path = GetFullPath(info);
        if (path == null)
            return (false, null, "No directory path returned");
        if (!dryrun)
        {
            var dirName = Path.GetDirectoryName(path);
            if (dirName == null)
                return (false, null, "GetDirectoryName failed");
            Directory.CreateDirectory(dirName);
        }
        FileInfo file = new(path);
        FileStream? stream = null;
        if (!dryrun)
            if (!file.Exists)
                stream = file.Create();
        stream ??= file.OpenRead();
        if (stream.Length > 0)
            return (true, file, "Path already exists");
        stream.Dispose();
        if (!dryrun)
        {
            JsonObject? jsonNode;
            JsonObject newNode;
            if (quest == null)
            {
                jsonNode = (JsonObject)JsonSerializer.SerializeToNode(CreateQuestRoot(info), JsonOptions.Default)!;
                newNode = new()
                {
                    {
                        "$schema",
                        "https://qstxiv.github.io/schema/quest-v1.json"
                    }
                };
                foreach ((string key, JsonNode? value) in jsonNode)
                    newNode.Add(key, value?.DeepClone());
            }
            else
            {
                newNode = (JsonObject)JsonSerializer.SerializeToNode(quest.Root, JsonOptions.Default)!;
            }
            using FileStream writeStream = file.OpenWrite();
            using Utf8JsonWriter writer = new(writeStream, new()
            {
                Encoder = JsonOptions.Default.Encoder,
                Indented = JsonOptions.Default.WriteIndented
            });
            newNode.WriteTo(writer, JsonOptions.Default);
        }
        return (true, file, $"File created{(dryrun ? " (dry run)" : "")}");
    }

    public static (bool Success, string Message) SaveUserPath(QuestInfo info, QuestRoot root)
    {
        string directory = Path.Combine(Svc.PluginInterface.GetPluginConfigDirectory(), "Quests");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, GetFilename(info));

        JsonObject serialized = (JsonObject)JsonSerializer.SerializeToNode(root, JsonOptions.Default)!;
        JsonObject withSchema = new()
        {
            {
                "$schema",
                "https://qstxiv.github.io/schema/quest-v1.json"
            }
        };
        foreach ((string key, JsonNode? value) in serialized)
            withSchema.Add(key, value?.DeepClone());

        using (FileStream stream = new(path, FileMode.Create))
        using (Utf8JsonWriter writer = new(stream, new()
        {
            Encoder = JsonOptions.Default.Encoder,
            Indented = JsonOptions.Default.WriteIndented
        }))
        {
            withSchema.WriteTo(writer, JsonOptions.Default);
        }

        Svc.Log.Information($"Saved user quest path to {path}");
        return (true, path);
    }

    public static string OpenEditorDescription = _L("Clicking this button writes the quest path to a file and opens it in your default text editor.") +
               _L("After making a change, click Reload Data below.") + "\n" +
               _L("To revert to the official version, delete the file and click Reload Data again.") + "\n" +
               _L("Left click: Open this quest in your default .json text editor\nRight click: Open Quests folder");
    public static (bool, string) OpenEditor(IQuestInfo info) => OpenEditor((QuestInfo)info);
    public static (bool, string) OpenEditor(QuestInfo info) => OpenEditor(GetFilename(info), info);
    public (bool, string) OpenEditor(ushort questId)
    {
        if (TryGetQuest(new QuestId(questId), out Quest? quest))
            return OpenEditor(GetFilename(quest.Info), (QuestInfo)quest.Info, quest);
        return OpenEditor(_questData.GetQuestInfo(new QuestId(questId)));
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

    public static (bool, string) OpenEditor(string filename, QuestInfo info, Quest? quest = null)
    {
        var parentDirectory = GetQuestPathsDirectory();
        DirectoryInfo? targetFolder = Directory.CreateDirectory(parentDirectory);
        if (targetFolder == null)
            return (false, "couldn't find QuestPaths folder");
        FileInfo? file = FindFilenameInDirectory(targetFolder, filename);
        if (file == null)
        {
            (bool success, FileInfo? path, string message) = CreatePath(info, quest);
            Svc.Log.Debug($"CreatePath: {success}, {path}, {message}");
            if (success && path != null)
                file = path;
            else
                return (false, $"couldn't find {filename}");
        }
        Process.Start(new ProcessStartInfo
        {
            FileName = file.FullName,
            WorkingDirectory = file.DirectoryName,
            UseShellExecute = true
        });
        return (true, file.FullName);
    }

    public static void OpenFolder()
    {
        var parentDirectory = GetQuestPathsDirectory();
        Directory.CreateDirectory(parentDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = parentDirectory,
            UseShellExecute = true,
            Verb = "open"
        });
    }

    public static FileInfo? FindFilenameInDirectory(DirectoryInfo root, string filename)
    {
        foreach (FileInfo file in root.GetFiles())
        {
            if (file.Name.Equals(filename, StringComparison.OrdinalIgnoreCase) || // if filename match case insensitive
                file.Name.StartsWith(filename[..(filename.IndexOf('_') + 1)])) // if ID at start of filename match
                return file;
        }

        foreach (DirectoryInfo directory in root.GetDirectories())
        {
            if (FindFilenameInDirectory(directory, filename) is FileInfo result)
                return result;
        }

        return null;
    }
}
