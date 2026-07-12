using System.Text.Json.Nodes;
using Questionable.Controller.Steps.Fishing;
using Questionable.Model.Questing;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class FishingPresetGeneratorTests
{
  [Fact]
  public void ApplyHookType_Null_EnablesAllHooksets()
  {
    var baitPreset = LoadBaitPreset();

    FishingPresetGenerator.ApplyHookType(baitPreset, hookType: null);

    Assert.True(HooksetEnabled(baitPreset, "PatienceWeak"));
    Assert.True(HooksetEnabled(baitPreset, "DoubleWeak"));
    Assert.True(HooksetEnabled(baitPreset, "TripleLegendary"));
    Assert.True(UseDoubleHook(baitPreset));
    Assert.True(UseTripleHook(baitPreset));
  }

  [Fact]
  public void ApplyHookType_NormalWeak_EnablesOnlyPatienceWeak()
  {
    var baitPreset = LoadBaitPreset();

    FishingPresetGenerator.ApplyHookType(baitPreset, new HookType
    {
      Normal = HookTypeFilter.From(new Hookset { Weak = true }),
    });

    Assert.True(HooksetEnabled(baitPreset, "PatienceWeak"));
    Assert.False(HooksetEnabled(baitPreset, "PatienceStrong"));
    Assert.False(HooksetEnabled(baitPreset, "DoubleWeak"));
    Assert.False(HooksetEnabled(baitPreset, "TripleWeak"));
    Assert.False(UseDoubleHook(baitPreset));
    Assert.False(UseTripleHook(baitPreset));
  }

  [Fact]
  public void ApplyHookType_NormalAll_EnablesAllPatienceHooksets()
  {
    var baitPreset = LoadBaitPreset();

    FishingPresetGenerator.ApplyHookType(baitPreset, new HookType
    {
      Normal = HookTypeFilter.AllHooksets,
    });

    Assert.True(HooksetEnabled(baitPreset, "PatienceWeak"));
    Assert.True(HooksetEnabled(baitPreset, "PatienceStrong"));
    Assert.True(HooksetEnabled(baitPreset, "PatienceLegendary"));
    Assert.False(HooksetEnabled(baitPreset, "DoubleWeak"));
  }

  [Fact]
  public void ApplyHookType_NormalAllAndDoubleStrong_EnablesExpectedHooksets()
  {
    var baitPreset = LoadBaitPreset();

    FishingPresetGenerator.ApplyHookType(baitPreset, new HookType
    {
      Normal = HookTypeFilter.AllHooksets,
      Double = HookTypeFilter.From(new Hookset { Strong = true }),
    });

    Assert.True(HooksetEnabled(baitPreset, "PatienceWeak"));
    Assert.True(HooksetEnabled(baitPreset, "PatienceStrong"));
    Assert.False(HooksetEnabled(baitPreset, "DoubleWeak"));
    Assert.True(HooksetEnabled(baitPreset, "DoubleStrong"));
    Assert.False(HooksetEnabled(baitPreset, "TripleWeak"));
    Assert.True(UseDoubleHook(baitPreset));
    Assert.False(UseTripleHook(baitPreset));
  }

  [Fact]
  public void ExportPreset_IsAh6PrefixedGzipBase64Json()
  {
    var preset = new JsonObject { ["PresetName"] = "2086-The Icepick Challenge" };

    string exported = FishingPresetGenerator.ExportPreset(preset);

    Assert.StartsWith("AH6_", exported);
  }

  private static JsonObject LoadBaitPreset()
  {
    using Stream stream = typeof(FishingPresetGenerator).Assembly.GetManifestResourceStream(
        "Questionable.Controller.Steps.Fishing.FishingPreset_Bait.json") ??
    throw new InvalidOperationException("Preset FishingPreset_Bait.json was not found");
    using StreamReader reader = new(stream);
    var preset = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
    preset["UniqueId"] = Guid.NewGuid().ToString();
    return preset;
  }

  private static bool HooksetEnabled(JsonObject baitPreset, string key) =>
      baitPreset["NormalHook"]![key]!["HooksetEnabled"]!.GetValue<bool>();

  private static bool UseDoubleHook(JsonObject baitPreset) =>
      baitPreset["NormalHook"]!["UseDoubleHook"]!.GetValue<bool>();

  private static bool UseTripleHook(JsonObject baitPreset) =>
      baitPreset["NormalHook"]!["UseTripleHook"]!.GetValue<bool>();
}
