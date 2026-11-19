using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Questionable.Utils;

internal static partial class QuestPathJson
{
    internal const string SchemaUrl = "https://qstxiv.github.io/schema/quest-v2.json";

    internal static string Write(JsonObject root, JsonSerializerOptions options) =>
        OneLinePositions().Replace(ToFile(root).ToJsonString(options), "[$1, $2, $3]");

    internal static JsonObject ToFile(JsonObject root)
    {
        JsonObject file = new() { ["$schema"] = SchemaUrl };
        foreach ((string key, JsonNode? value) in root)
        {
            if (key != "$schema")
                file[key] = value?.DeepClone();
        }

        if (file["QuestSequence"] is JsonArray sequences)
        {
            for (int i = 0; i < sequences.Count; i++)
            {
                if (sequences[i] is JsonObject sequence && WithSequenceZone(sequence) is { } moved)
                    sequences[i] = moved;
            }
        }

        return file;
    }

    private static JsonObject? WithSequenceZone(JsonObject sequence)
    {
        if (sequence["Steps"] is not JsonArray steps)
            return null;

        List<uint> zones = steps.OfType<JsonObject>()
            .Select(x => x["TerritoryId"]?.GetValue<uint>())
            .OfType<uint>()
            .ToList();
        if (zones.Count == 0)
            return null;

        uint zone = zones.GroupBy(x => x)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => zones.IndexOf(x.Key))
            .First().Key;
        foreach (JsonObject step in steps.OfType<JsonObject>())
        {
            if (step["TerritoryId"]?.GetValue<uint>() == zone)
                step.Remove("TerritoryId");
        }

        JsonObject result = [];
        foreach ((string key, JsonNode? value) in sequence)
        {
            if (key == "TerritoryId")
                continue;
            result[key] = value?.DeepClone();
            if (key == "Sequence")
                result["TerritoryId"] = zone;
        }

        if (!result.ContainsKey("TerritoryId"))
            result["TerritoryId"] = zone;
        return result;
    }

    [GeneratedRegex(@"\[\s*(-?[0-9][0-9.eE+-]*),\s*(-?[0-9][0-9.eE+-]*),\s*(-?[0-9][0-9.eE+-]*)\s*\]")]
    private static partial Regex OneLinePositions();
}
