using System.Diagnostics.CodeAnalysis;
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
    internal static Configuration Instance()
    {
        var pluginConfig = Svc.PluginInterface.GetPluginConfig();
        if (pluginConfig is Configuration config)
            return config;
        throw new Exception(_L("Could not get plugin config"));
    }
    #region Serialised data
    // Variables that are not affected by profile switches
    public const int PluginSetupVersion = 5;
    public const int PluginConfigVersion = 2;
    public int PluginSetupCompleteVersion { get; set; }
    // Persisted base sections
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

    /// <summary>
    /// One-time flag: the release that defaulted auto-redeem off forces it off once on first load,
    /// then never touches <see cref="AdvancedConfiguration.AutoRedeemRewardItems"/> again.
    /// </summary>
    public bool AutoRedeemOffResetApplied { get; set; }

    // --- Persisted profile data ---
    /// <summary>
    /// Named profiles. Each profile is a sparse patch — only fields that differ from
    /// the base configuration are stored. Sections and fields absent from the patch
    /// fall through to the base values transparently.
    /// </summary>
    public Dictionary<string, Dictionary<string, JObject>> Profiles { get; set; } = [];

    /// <summary>
    /// Maps a character's PlayerState.ContentId to a named profile in
    /// <see cref="Profiles"/>. Characters with no entry use the base configuration.
    /// </summary>
    public Dictionary<string, string> CharacterProfiles { get; set; } = [];
    #endregion

    internal bool IsPluginSetupComplete() => PluginSetupCompleteVersion == PluginSetupVersion;
    internal void MarkPluginSetupComplete() => PluginSetupCompleteVersion = PluginSetupVersion;

    // Public configuration surface; falls through to the base configuration when no profile is active
    [JsonIgnore] public GeneralConfiguration General => _activeProfileGeneral ?? _general;
    [JsonIgnore] public StopConfiguration Stop => _activeProfileStop ?? _stop;
    [JsonIgnore] public DutyConfiguration Duties => _activeProfileDuties ?? _duties;
    [JsonIgnore] public SinglePlayerDutyConfiguration SinglePlayerDuties => _activeProfileSinglePlayerDuties ?? _singlePlayerDuties;
    [JsonIgnore] public NotificationConfiguration Notifications => _activeProfileNotifications ?? _notifications;
    [JsonIgnore] public AdvancedConfiguration Advanced => _activeProfileAdvanced ?? _advanced;

    /// <summary>
    /// Returns the base (non-profiled) instance of a section for use in the
    /// config UI when editing base values.
    /// </summary>
    internal GeneralConfiguration BaseGeneral => _general;
    internal StopConfiguration BaseStop => _stop;
    internal DutyConfiguration BaseDuties => _duties;
    internal SinglePlayerDutyConfiguration BaseSinglePlayerDuties => _singlePlayerDuties;
    internal NotificationConfiguration BaseNotifications => _notifications;
    internal AdvancedConfiguration BaseAdvanced => _advanced;

    #region Character Profiles
    // Runtime-only merged section code (never serialised)
    [JsonIgnore] private GeneralConfiguration? _activeProfileGeneral;
    [JsonIgnore] private StopConfiguration? _activeProfileStop;
    [JsonIgnore] private DutyConfiguration? _activeProfileDuties;
    [JsonIgnore] private SinglePlayerDutyConfiguration? _activeProfileSinglePlayerDuties;
    [JsonIgnore] private NotificationConfiguration? _activeProfileNotifications;
    [JsonIgnore] private AdvancedConfiguration? _activeProfileAdvanced;

    /// <summary>
    /// Activates the profile associated with the given character content ID, merging
    /// it over the base configuration. Pass null or an unmapped ID to revert to base.
    /// </summary>
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

    /// <summary>
    /// Activates a named profile directly, regardless of current character.
    /// Useful for preview/testing in the config UI.
    /// </summary>
    internal void ActivateProfile(string profileName)
    {
        if (!Profiles.TryGetValue(profileName, out var patches))
        {
            ClearActiveProfile();
            return;
        }

        ApplyPatches(profileName, patches);
    }

    /// <summary>
    /// Reverts to the base configuration (no profile active).
    /// </summary>
    internal void ClearActiveProfile()
    {
        ActiveProfileName = null;
        _activeProfileGeneral = null;
        _activeProfileStop = null;
        _activeProfileDuties = null;
        _activeProfileSinglePlayerDuties = null;
        _activeProfileNotifications = null;
        _activeProfileAdvanced = null;
    }

    /// <summary>
    /// Returns the name of the currently active profile, or null if using base config.
    /// </summary>
    [JsonIgnore] internal string? ActiveProfileName { get; private set; }

    private void ApplyPatches(string profileName, Dictionary<string, JObject> patches)
    {
        ActiveProfileName = profileName;
        _activeProfileGeneral = MergeSection(_general, patches, "General");
        _activeProfileStop = MergeSection(_stop, patches, "Stop");
        _activeProfileDuties = MergeSection(_duties, patches, "Duties");
        _activeProfileSinglePlayerDuties = MergeSection(_singlePlayerDuties, patches, "SinglePlayerDuties");
        _activeProfileNotifications = MergeSection(_notifications, patches, "Notifications");
        _activeProfileAdvanced = MergeSection(_advanced, patches, "Advanced");
    }

    /// <summary>
    /// Serialises <paramref name="base"/> to a JObject, merges the patch for
    /// <paramref name="sectionKey"/> on top, and deserialises the result.
    /// Returns null (fall-through to base) if the section is absent from the patch.
    /// </summary>
    private static T? MergeSection<T>(T @base, Dictionary<string, JObject> patches, string sectionKey)
        where T : class
    {
        if (!patches.TryGetValue(sectionKey, out var patch))
            return null;

        var merged = JObject.FromObject(@base);
        merged.Merge(patch, new JsonMergeSettings
        {
            MergeArrayHandling = MergeArrayHandling.Replace,
            MergeNullValueHandling = MergeNullValueHandling.Merge,
        });
        return merged.ToObject<T>();
    }

    /// <summary>
    /// Creates a new empty (no-op) profile with the given name.
    /// </summary>
    internal void CreateProfile(string profileName)
    {
        Profiles.TryAdd(profileName, []);
    }

    /// <summary>
    /// Removes a profile and any character bindings that reference it.
    /// </summary>
    internal void DeleteProfile(string profileName)
    {
        Profiles.Remove(profileName);

        // Clean up any character bindings pointing at the deleted profile
        var toRemove = new List<string>();
        foreach (var (charId, name) in CharacterProfiles)
            if (name == profileName)
                toRemove.Add(charId);
        foreach (var charId in toRemove)
            CharacterProfiles.Remove(charId);

        if (ActiveProfileName == profileName)
            ClearActiveProfile();
    }
    #endregion

    #region Variables
    internal sealed class GeneralConfiguration
    {
        public ECombatModule CombatModule { get; set; } = ECombatModule.None;
        public uint MountId { get; set; } = 71;
        public string ChocoboName { get; set; } = "Chicken";
        public GrandCompany GrandCompany { get; set; } = GrandCompany.None;
        public Job CombatJob { get; set; } = Job.ADV;
        public Job CraftingJob { get; set; } = Job.CRP;
        public Job GatheringJob { get; set; } = Job.MIN;
        public EGearsetUpdateSource GearsetUpdateSource { get; set; } = EGearsetUpdateSource.Vanilla;
        public bool HideInAllInstances { get; set; } = true;
        public bool UseQuestionableTheme { get; set; } = true;
        public bool TitleBarPillCenter { get; set; } = true;
        public bool UseEscToCancelQuesting { get; set; } = true;
        public bool ShowIncompleteSeasonalEvents { get; set; } = true;
        public bool SkipLowPriorityDuties { get; set; }
        public bool ConfigureTextAdvance { get; set; } = true;
        public bool DontSkipCutscenes { get; set; }
        public bool DontShowAnswerSuggestions { get; set; }
        public bool AutoStepRefreshEnabled { get; set; }
        public int AutoStepRefreshDelaySeconds { get; set; } = 30;
        public bool UseTickets { get; set; }
        public bool HideSponsorButton { get; set; }
        public bool DismissedReportWarning { get; set; }
        public bool ReportsDisabled { get; set; }
        public string ReportMessage { get; set; } = "";
        public string DisplayName { get; set; } = "Anonymous";
        public string Language { get; set; } = "en";
        public bool HideRemainingTasks { get; set; }
        public bool ClaimMail { get; set; }
        public bool SameJobThroughoutQuest { get; set; }
        public bool QuestIcons { get; set; } = true;
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
        public bool PreventQuestCompletion { get; set; }
        public bool AbandonQuestBeforeCompletion { get; set; }
        public bool RemoveFromPriorityWhenAbandoned { get; set; }
        public bool ShowWindowOnStart { get; set; }
        public bool StartMinimized { get; set; }
        public bool OpenEditor { get; set; }
        public bool NamazuPreferCraft { get; set; }
        public bool Debug { get; set; }
        public bool DebugLocalisation { get; set; }

        /// <summary>
        ///     Gates the experimental questpath auto-generation (Journal Progress right-click). Generated paths
        ///     are unreviewed machine drafts and must not be run unattended.
        /// </summary>
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
        /// <summary>Whether to automatically download newer quest/gathering path bundles.</summary>
        public bool AutoUpdate { get; set; } = true;

        /// <summary>Data version of the path bundle currently in <c>{ConfigDirectory}/PathData/</c>; 0 if none.</summary>
        public long InstalledDataVersion { get; set; }

        /// <summary>Plugin version that downloaded the current bundle; used to discard it after a plugin update.</summary>
        public string? BundlePluginVersion { get; set; }

        /// <summary>When the updater last checked for a newer bundle.</summary>
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