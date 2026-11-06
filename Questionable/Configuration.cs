using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using Dalamud.Configuration;
using Dalamud.Game.Text;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Windows.Common;
using GrandCompany = FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany;
namespace Questionable;

internal sealed class Configuration : IPluginConfiguration
{
    private static Configuration? _runtimeInstance;

    // Static game helpers must use the same live profile as injected consumers.
    // GetPluginConfig deserializes the saved base settings without activating a profile.
    internal static Configuration Instance() => Volatile.Read(ref _runtimeInstance)
        ?? throw new InvalidOperationException("Runtime configuration has not been initialized.");

    internal static void InitializeRuntime(Configuration configuration) =>
        Interlocked.Exchange(ref _runtimeInstance, configuration);

    internal static void ClearRuntime(Configuration configuration) =>
        Interlocked.CompareExchange(ref _runtimeInstance, null, configuration);
    #region Serialised data
    public const int PluginSetupVersion = 5;
    public const int PluginConfigVersion = 2;
    public int PluginSetupCompleteVersion { get; set; }
    [JsonProperty(nameof(General))] private GeneralConfiguration _general { get; } = new();
    [JsonProperty(nameof(Stop))] private StopConfiguration _stop { get; } = new();
    [JsonProperty(nameof(Duties))] private DutyConfiguration _duties { get; } = new();
    [JsonProperty(nameof(SinglePlayerDuties))] private SinglePlayerDutyConfiguration _singlePlayerDuties { get; } = new();
    [JsonProperty(nameof(Notifications))] private NotificationConfiguration _notifications { get; } = new();
    [JsonProperty(nameof(Advanced))] private AdvancedConfiguration _advanced { get; } = new();
    public WindowConfig DebugWindowConfig { get; } = new();
    public WindowConfig ConfigWindowConfig { get; } = new();
    public PriorityConfiguration Priority { get; } = new();
    public PathDataConfiguration PathData { get; } = new();
    public int Version { get; set; } = PluginConfigVersion;

    public bool AutoRedeemOffResetApplied { get; set; }

    public Dictionary<string, Dictionary<string, JObject>> Profiles { get; set; } = [];

    public Dictionary<string, string> CharacterProfiles { get; set; } = [];
    #endregion

    internal bool IsPluginSetupComplete() => PluginSetupCompleteVersion == PluginSetupVersion;
    internal void MarkPluginSetupComplete() => PluginSetupCompleteVersion = PluginSetupVersion;

    [JsonIgnore] public GeneralConfiguration General => _activeProfileGeneral ?? _general;
    [JsonIgnore] public StopConfiguration Stop => _activeProfileStop ?? _stop;
    [JsonIgnore] public DutyConfiguration Duties => _activeProfileDuties ?? _duties;
    [JsonIgnore] public SinglePlayerDutyConfiguration SinglePlayerDuties => _activeProfileSinglePlayerDuties ?? _singlePlayerDuties;
    [JsonIgnore] public NotificationConfiguration Notifications => _activeProfileNotifications ?? _notifications;
    [JsonIgnore] public AdvancedConfiguration Advanced => _activeProfileAdvanced ?? _advanced;

    internal GeneralConfiguration BaseGeneral => _general;
    internal StopConfiguration BaseStop => _stop;
    internal DutyConfiguration BaseDuties => _duties;
    internal SinglePlayerDutyConfiguration BaseSinglePlayerDuties => _singlePlayerDuties;
    internal NotificationConfiguration BaseNotifications => _notifications;
    internal AdvancedConfiguration BaseAdvanced => _advanced;

    #region Character Profiles
    internal event Action<string>? LanguageChanged;

    internal void SetLanguage(string language)
    {
        string previousLanguage = General.Language;
        General.Language = language;
        NotifyLanguageChanged(previousLanguage);
    }

    private void NotifyLanguageChanged(string previousLanguage)
    {
        if (!string.Equals(previousLanguage, General.Language, StringComparison.OrdinalIgnoreCase))
            LanguageChanged?.Invoke(General.Language);
    }

    [JsonIgnore] private GeneralConfiguration? _activeProfileGeneral;
    [JsonIgnore] private StopConfiguration? _activeProfileStop;
    [JsonIgnore] private DutyConfiguration? _activeProfileDuties;
    [JsonIgnore] private SinglePlayerDutyConfiguration? _activeProfileSinglePlayerDuties;
    [JsonIgnore] private NotificationConfiguration? _activeProfileNotifications;
    [JsonIgnore] private AdvancedConfiguration? _activeProfileAdvanced;

    internal void ActivateForCharacter(string contentId)
    {
        if (!CharacterProfiles.TryGetValue(contentId, out var profileName) ||
            !Profiles.TryGetValue(profileName, out var patches))
        {
            ClearActiveProfile();
            return;
        }

        ApplyPatches(profileName, patches);
    }

    internal void ActivateProfile(string profileName)
    {
        if (!Profiles.TryGetValue(profileName, out var patches))
        {
            ClearActiveProfile();
            return;
        }

        ApplyPatches(profileName, patches);
    }

    internal void ClearActiveProfile()
    {
        string previousLanguage = General.Language;
        CaptureActiveProfile();
        ActiveProfileName = null;
        _activeProfileGeneral = null;
        _activeProfileStop = null;
        _activeProfileDuties = null;
        _activeProfileSinglePlayerDuties = null;
        _activeProfileNotifications = null;
        _activeProfileAdvanced = null;
        NotifyLanguageChanged(previousLanguage);
    }

    [JsonIgnore] internal string? ActiveProfileName { get; private set; }

    private void ApplyPatches(string profileName, Dictionary<string, JObject> patches)
    {
        string previousLanguage = General.Language;
        CaptureActiveProfile();
        ActiveProfileName = profileName;
        _activeProfileGeneral = MergeSection(_general, patches, "General");
        _activeProfileStop = MergeSection(_stop, patches, "Stop");
        _activeProfileDuties = MergeSection(_duties, patches, "Duties");
        _activeProfileSinglePlayerDuties = MergeSection(_singlePlayerDuties, patches, "SinglePlayerDuties");
        _activeProfileNotifications = MergeSection(_notifications, patches, "Notifications");
        _activeProfileAdvanced = MergeSection(_advanced, patches, "Advanced");
        NotifyLanguageChanged(previousLanguage);
    }

    private static T MergeSection<T>(T @base, Dictionary<string, JObject> patches, string sectionKey)
        where T : class
    {
        var merged = JObject.FromObject(@base);
        if (patches.TryGetValue(sectionKey, out var patch))
        {
            merged.Merge(patch, new JsonMergeSettings
            {
                MergeArrayHandling = MergeArrayHandling.Replace,
                MergeNullValueHandling = MergeNullValueHandling.Merge,
            });
        }

        return merged.ToObject<T>()!;
    }

    [OnSerializing]
    internal void CaptureActiveProfile(StreamingContext context = default)
    {
        if (ActiveProfileName == null || !Profiles.TryGetValue(ActiveProfileName, out var patches))
            return;

        CapturePatch(patches, "General", _general, _activeProfileGeneral);
        CapturePatch(patches, "Stop", _stop, _activeProfileStop);
        CapturePatch(patches, "Duties", _duties, _activeProfileDuties);
        CapturePatch(patches, "SinglePlayerDuties", _singlePlayerDuties, _activeProfileSinglePlayerDuties);
        CapturePatch(patches, "Notifications", _notifications, _activeProfileNotifications);
        CapturePatch(patches, "Advanced", _advanced, _activeProfileAdvanced);
    }

    private static void CapturePatch<T>(Dictionary<string, JObject> patches, string sectionKey, T @base, T? active)
        where T : class
    {
        if (active == null)
            return;

        JObject baseJson = JObject.FromObject(@base);
        JObject patch = [];
        foreach (JProperty property in JObject.FromObject(active).Properties())
        {
            if (!JToken.DeepEquals(property.Value, baseJson[property.Name]))
                patch[property.Name] = property.Value.DeepClone();
        }

        if (patch.Count == 0)
            patches.Remove(sectionKey);
        else
            patches[sectionKey] = patch;
    }

    internal void CreateProfile(string profileName)
    {
        Profiles.TryAdd(profileName, []);
    }

    internal void DeleteProfile(string profileName)
    {
        Profiles.Remove(profileName);

        var toRemove = new List<string>();
        foreach (var (charId, name) in CharacterProfiles)
            if (name == profileName)
                toRemove.Add(charId);
        foreach (var charId in toRemove)
            CharacterProfiles.Remove(charId);

        if (ActiveProfileName == profileName)
            ClearActiveProfile();
    }

    internal bool RenameProfile(string oldName, string newName)
    {
        if (oldName == newName || Profiles.ContainsKey(newName) || !Profiles.Remove(oldName, out var patches))
            return false;

        Profiles[newName] = patches;
        foreach (var charId in CharacterProfiles.Where(x => x.Value == oldName).Select(x => x.Key).ToList())
            CharacterProfiles[charId] = newName;

        if (ActiveProfileName == oldName)
            ActiveProfileName = newName;
        return true;
    }

    internal void AssignProfile(string contentId, string? profileName)
    {
        if (profileName == null || !Profiles.ContainsKey(profileName))
            CharacterProfiles.Remove(contentId);
        else
            CharacterProfiles[contentId] = profileName;
    }

    internal string? GetProfileForCharacter(string contentId) =>
        CharacterProfiles.TryGetValue(contentId, out var profileName) && Profiles.ContainsKey(profileName)
            ? profileName
            : null;
    #endregion

    #region Variables
    internal sealed class GeneralConfiguration
    {
        public string Language { get; set; } = "en";
        public ECombatModule CombatModule { get; set; } = ECombatModule.None;
        public uint MountId { get; set; } = 1;
        public GrandCompany GrandCompany { get; set; } = GrandCompany.None;
        public Job CombatJob { get; set; } = Job.ADV;
        public Job CraftingJob { get; set; } = Job.CRP;
        public Job GatheringJob { get; set; } = Job.MIN;
        public EGearsetUpdateSource GearsetUpdateSource { get; set; } = EGearsetUpdateSource.Vanilla;
        public string ChocoboName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = "Anonymous";
        public bool UseEscToCancelQuesting { get; set; } = true;
        public bool UseQuestionableTheme { get; set; } = true;
        public bool ShowIncompleteSeasonalEvents { get; set; } = true;
        public bool ShowCompleteSeasonalEvents { get; set; }
        public bool QuestIcons { get; set; } = true;
        public bool HideSponsorButton { get; set; }
        public bool HideRemainingTasks { get; set; }
        public bool HideInAllInstances { get; set; } = true;
        public bool HidePatch { get; set; }
        public bool HideQuestStartedJob { get; set; }
        public bool HideThingsNotAvailable { get; set; }
        public bool HideQuestRequiredLevel { get; set; }
        public bool ConfigureTextAdvance { get; set; } = true;
        public bool DontSkipCutscenes { get; set; }
        public bool DontShowAnswerSuggestions { get; set; }
        public bool SkipLowPriorityDuties { get; set; }
        public bool UseTickets { get; set; }
        public bool SameJobThroughoutQuest { get; set; }
        public bool IgnoreNextQuestIfPrioSet { get; set; }
        public bool TitleBarPillCenter { get; set; } = true;
        public bool DismissedReportWarning { get; set; }
        public bool ReportsDisabled { get; set; }
        public string ReportMessage { get; set; } = "";
        public bool ClaimMail { get; set; }
        public bool AutoStepRefreshEnabled { get; set; }
        public int AutoStepRefreshDelaySeconds { get; set; } = 30;
    }

    internal sealed class StopConfiguration
    {
        public bool Enabled { get; set; }

        [JsonProperty(ItemConverterType = typeof(ElementIdNConverter))]
        public List<ElementId> QuestsToStopAfter { get; set; } = [];

        [JsonProperty(ItemConverterType = typeof(ElementIdNConverter))]
        public List<ElementId> QuestsToStopWhenAccepted { get; set; } = [];

        public bool LevelToStopAfter { get; set; }
        public int TargetLevel { get; set; } = 50;
        public bool RunCommandAfterStop { get; set; }
        public string CommandAfterStop { get; set; } = "/li auto";
        public bool RemoveWhenCompleteConditionMet { get; set; }
    }

    internal sealed class DutyConfiguration
    {
        public bool RunInstancedContentWithAutoDuty { get; set; }
        public bool RunUnsynced { get; set; } = true;
        public HashSet<uint> WhitelistedDutyCfcIds { get; set; } = [];
        public HashSet<uint> BlacklistedDutyCfcIds { get; set; } = [];
        public Dictionary<string, bool> ExpansionHeaderStates { get; set; } = [];
    }

    internal sealed class SinglePlayerDutyConfiguration
    {
        public bool RunSoloInstancesWithBossMod { get; set; }

        [SuppressMessage("Performance", "CA1822", Justification = "Will be fixed when no longer WIP")]
        public byte RetryDifficulty => 2;

        public HashSet<uint> WhitelistedSinglePlayerDutyCfcIds { get; set; } = [];
        public HashSet<uint> BlacklistedSinglePlayerDutyCfcIds { get; set; } = [];
        public Dictionary<string, bool> HeaderStates { get; set; } = [];
    }

    internal sealed class NotificationConfiguration
    {
        public bool Enabled { get; set; } = true;
        public XivChatType ChatType { get; set; } = XivChatType.Debug;
        public bool ShowTrayMessage { get; set; }
        public bool FlashTaskbar { get; set; }
        public bool NotifyOnStopCondition { get; set; }
        public bool NotifyOnCriticalFailure { get; set; } = true;
    }

    internal sealed class AdvancedConfiguration
    {
        public bool DebugOverlay { get; set; }
        public bool ShowSavedPos { get; set; }
        public bool ShowFlagPos { get; set; }
        public bool CombatDataOverlay { get; set; }
        public bool HighlightSelectedNpc { get; set; } = true;
        public ObjectHighlightColor HighlightColor { get; set; } = ObjectHighlightColor.Yellow;
        public bool NeverFly { get; set; }
        public bool AdditionalStatusInformation { get; set; }
        public bool ShowTracked { get; set; }
        public bool ShowDailies { get; set; }
        public bool ShowDirector { get; set; }
        public bool ShowActionManager { get; set; }
        public bool ShowNewGamePlus { get; set; }
        public bool ShowHoveredItem { get; set; }
        public bool DisableAutoDutyBareMode { get; set; }
        public bool SkipAetherCurrents { get; set; }
        public bool SkipClassJobQuests { get; set; }
        public bool SkipARealmRebornHardModePrimals { get; set; }
        public bool SkipCrystalTowerRaids { get; set; }
        public bool SkipFeistyLittleChocobo { get; set; }
        public bool StopMSQ { get; set; }
        public bool PreventQuestCompletion { get; set; }
        public bool AbandonQuestBeforeCompletion { get; set; }
        public bool RemoveFromPriorityWhenAbandoned { get; set; }
        public bool ShowWindowOnStart { get; set; }
        public bool StartMinimized { get; set; }
        public bool OpenEditor { get; set; }
        public bool NamazuPreferCraft { get; set; }
        public bool Debug { get; set; }
        public bool DebugLocalisation { get; set; }

        // Generated paths are unreviewed drafts and must not run unattended.
        public bool AllowPathGeneration { get; set; }
        public bool AutoRedeemRewardItems { get; set; }
        public HashSet<uint> AutoRedeemItemBlacklist { get; set; } = [];
    }

    internal void ApplyAutoRedeemRewardItemsInitialReset()
    {
        _advanced.AutoRedeemRewardItems = false;

        foreach (Dictionary<string, JObject> profile in Profiles.Values)
        {
            if (!profile.TryGetValue(nameof(Advanced), out JObject? advancedPatch))
                continue;

            advancedPatch.Remove(nameof(AdvancedConfiguration.AutoRedeemRewardItems));
            if (!advancedPatch.HasValues)
                profile.Remove(nameof(Advanced));
        }
    }

    internal sealed class PriorityConfiguration
    {
        public Dictionary<string, List<string>> Presets { get; set; } = [];
    }

    internal sealed class PathDataConfiguration
    {
        public bool AutoUpdate { get; set; } = true;

        public long InstalledDataVersion { get; set; }

        public string? BundlePluginVersion { get; set; }

        public DateTimeOffset? LastCheck { get; set; }
    }
    #endregion
    public sealed class ElementIdNConverter : JsonConverter<ElementId>
    {
        public override void WriteJson(JsonWriter writer, ElementId? value, JsonSerializer serializer) => writer.WriteValue(value?.ToString());

        public override ElementId? ReadJson(JsonReader reader, Type objectType, ElementId? existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            string? value = reader.Value?.ToString();
            return value != null ? ElementId.FromString(value) : null;
        }
    }
}
