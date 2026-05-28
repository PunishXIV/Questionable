using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Dalamud.Configuration;
using Dalamud.Game.Text;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Newtonsoft.Json;
using Questionable.Model.Questing;
using Questionable.Windows.Common;
using GrandCompany = FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany;
namespace Questionable;

internal sealed class Configuration : IPluginConfiguration
{
    public const int PluginSetupVersion = 5;
    public int PluginSetupCompleteVersion { get; set; }
    public GeneralConfiguration General { get; } = new();
    public StopConfiguration Stop { get; } = new();
    public DutyConfiguration Duties { get; } = new();
    public SinglePlayerDutyConfiguration SinglePlayerDuties { get; } = new();
    public NotificationConfiguration Notifications { get; } = new();
    public AdvancedConfiguration Advanced { get; } = new();
    public WindowConfig DebugWindowConfig { get; } = new();
    public WindowConfig ConfigWindowConfig { get; } = new();
    public PriorityConfiguration Priority { get; } = new();
    public PathDataConfiguration PathData { get; } = new();

    public int Version { get; set; } = 1;

    internal bool IsPluginSetupComplete() => PluginSetupCompleteVersion == PluginSetupVersion;

    internal void MarkPluginSetupComplete() => PluginSetupCompleteVersion = PluginSetupVersion;

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
        public bool UseEscToCancelQuesting { get; set; } = true;
        public bool ShowIncompleteSeasonalEvents { get; set; } = true;
        public bool SkipLowPriorityDuties { get; set; }
        public bool ConfigureTextAdvance { get; set; } = true;
        public bool DontSkipCutscenes { get; set; }
        public bool AutoStepRefreshEnabled { get; set; }
        public int AutoStepRefreshDelaySeconds { get; set; } = 30;
        public bool UseTickets { get; set; }
        public bool HideSponsorButton { get; set; }
        public bool DismissedReportWarning { get; set; }
        public bool ReportsDisabled { get; set; }
        public string ReportMessage { get; set; } = "";
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
    }

    internal sealed class DutyConfiguration
    {
        public bool RunInstancedContentWithAutoDuty { get; set; }
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

    internal enum EGearsetUpdateSource
    {
        Vanilla,
        Stylist
    }

    internal enum ECombatModule
    {
        None,
        BossMod,
        WrathCombo,
        RotationSolverReborn
    }

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
