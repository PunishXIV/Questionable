using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Questionable.Model.Questing;
using Questionable.Utils;
using static Questionable.Controller.Steps.Shared.Fish;

namespace Questionable.Controller.Steps.Fishing;

internal interface IFishingPresetGenerator
{
  /// <summary>
  /// Creates an AutoHook fishing preset from a fish task.
  /// </summary>
  /// <param name="task">The fish task to create a preset from.</param>
  /// <returns>The AutoHotkey fishing preset. Gzipped and base64 encoded.</returns>
  string CreatePresetFromTask(FishTask task);
}

internal sealed class FishingPresetGenerator(QuestRegistry questRegistry) : IFishingPresetGenerator
{
  private static readonly string[] HooksetKeys =
  [
      "PatienceWeak", "PatienceStrong", "PatienceLegendary",
      "DoubleWeak", "DoubleStrong", "DoubleLegendary",
      "TripleWeak", "TripleStrong", "TripleLegendary",
  ];

  /// <inheritdoc />
  public string CreatePresetFromTask(FishTask task)
  {
    if (task.GatheredItem is not { FishingOptions.BaitId: > 0 })
      throw new ArgumentException("Gathered item is invalid. Bait ID is required.");

    questRegistry.TryGetQuest(task.Quest.Id, out var quest);
    if (quest == null)
      throw new ArgumentException("Quest not found");

    var preset = LoadBasePreset($"{quest.Id}-{quest.Info.Name}");

    SetCollectability(preset, task.GatheredItem.Collectability);
    SetFishingOptions(preset, task.GatheredItem.FishingOptions);

    return ExportPreset(preset);
  }

  internal static string ExportPreset(JsonObject preset)
  {
    byte[] compressed = CompressUtils.Compress(Encoding.UTF8.GetBytes(preset.ToJsonString()));
    return "AH6_" + Convert.ToBase64String(compressed);
  }

  internal static void ApplyHookType(JsonObject baitPreset, HookType? hookType)
  {
    var normalHook = baitPreset["NormalHook"]!.AsObject();

    if (hookType is null)
    {
      EnableAllHooksets(normalHook);
      return;
    }

    ApplyHookTypeFilter(normalHook, "Patience", hookType.Normal);
    ApplyHookTypeFilter(normalHook, "Double", hookType.Double);
    ApplyHookTypeFilter(normalHook, "Triple", hookType.Triple);
  }

  private static JsonObject LoadBasePreset(string name)
  {
    Stream stream = typeof(FishingPresetGenerator).Assembly.GetManifestResourceStream(
        $"Questionable.Controller.Steps.Fishing.FishingPreset_Base.json") ??
    throw new InvalidOperationException("Preset FishingPreset_Base.json was not found");
    using StreamReader reader = new(stream);
    var preset = JsonNode.Parse(reader.ReadToEnd()) as JsonObject ?? throw new InvalidOperationException("Failed to parse FishingPreset_Base.json");
    preset["PresetName"] = name;
    preset["UniqueId"] = Guid.NewGuid().ToString();
    preset["ExtraCfg"]!["UniqueId"] = Guid.NewGuid().ToString();
    return preset;
  }

  private static JsonObject LoadBaitPreset()
  {
    Stream stream = typeof(FishingPresetGenerator).Assembly.GetManifestResourceStream(
        $"Questionable.Controller.Steps.Fishing.FishingPreset_Bait.json") ??
    throw new InvalidOperationException("Preset FishingPreset_Bait.json was not found");
    using StreamReader reader = new(stream);
    var preset = JsonNode.Parse(reader.ReadToEnd()) as JsonObject ?? throw new InvalidOperationException("Failed to parse FishingPreset_Bait.json");
    preset["UniqueId"] = Guid.NewGuid().ToString();
    return preset;
  }

  private static void SetCollectability(JsonObject preset, ushort collectability)
  {
    preset["AutoCastsCfg"]!["CastCollect"]!["Enabled"] = collectability != 0;
    preset["AutoCastsCfg"]!["TurnCollectOffWithoutAnimCancel"] = collectability == 0;
    preset["AutoCastsCfg"]!["TurnCollectOff"] = collectability == 0;
  }

  private static void SetFishingOptions(JsonObject preset, FishingOptions fishingOptions)
  {
    var baitPreset = AddBait(preset, fishingOptions.BaitId);
    ApplyHookType(baitPreset, fishingOptions.HookType);
  }

  private static JsonObject AddBait(JsonObject preset, uint baitId)
  {
    var baitPreset = LoadBaitPreset();
    baitPreset["BaitFish"]!["Id"] = baitId;
    preset["ListOfBaits"]!.AsArray().Add(baitPreset);
    preset["ExtraCfg"]!["ForcedBaitId"] = baitId;
    return baitPreset;
  }

  private static void EnableAllHooksets(JsonObject normalHook)
  {
    foreach (string key in HooksetKeys)
      EnableHookset(normalHook, key);

    normalHook["UseDoubleHook"] = true;
    normalHook["UseTripleHook"] = true;
  }

  private static void ApplyHookTypeFilter(JsonObject normalHook, string prefix, HookTypeFilter? filter)
  {
    if (filter is not { } hookTypeFilter)
      return;

    if (prefix is "Double")
      normalHook["UseDoubleHook"] = true;
    else if (prefix is "Triple")
      normalHook["UseTripleHook"] = true;

    if (hookTypeFilter.IsAllHooksets)
    {
      EnableHookset(normalHook, $"{prefix}Weak");
      EnableHookset(normalHook, $"{prefix}Strong");
      EnableHookset(normalHook, $"{prefix}Legendary");
      return;
    }

    Hookset? hookset = hookTypeFilter.Hookset;
    if (hookset is null)
      return;

    if (hookset.Weak is true)
      EnableHookset(normalHook, $"{prefix}Weak");
    if (hookset.Strong is true)
      EnableHookset(normalHook, $"{prefix}Strong");
    if (hookset.Legendary is true)
      EnableHookset(normalHook, $"{prefix}Legendary");
  }

  private static void EnableHookset(JsonObject normalHook, string key) =>
      normalHook[key]!["HooksetEnabled"] = true;
}
