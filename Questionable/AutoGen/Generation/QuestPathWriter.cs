// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

using System.Collections;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Quest = Lumina.Excel.Sheets.Quest;

namespace Questionable.AutoGen.Generation;

internal static class QuestPathWriter
{
    private const string SchemaUrl = "https://qstxiv.github.io/schema/quest-v1.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                NoEmptyCollectionModifier,
                AlwaysSerializeModifier,
                DefaultTrueModifier,
                SkipDefaultObjectModifier
            }
        }
    };

    internal static string FileName(Quest quest) =>
        $"{quest.RowId & 0xFFFF}_{SimplifyName(QuestGameData.QuestName(quest))}.json";

    private static string SimplifyName(string name)
    {
        foreach (char invalid in new[] { '.', '*', '"', '/', '\\', '<', '>', '|', ':', '?' })
            name = name.Replace(invalid.ToString(), string.Empty, StringComparison.Ordinal);

        return name.Trim();
    }

    internal static string Serialize(QuestPathResult result)
    {
        JsonObject serialized = (JsonObject)JsonSerializer.SerializeToNode(result.Root, Options)!;

        JsonObject output = new() { ["$schema"] = SchemaUrl };
        foreach ((string key, JsonNode? value) in serialized)
            output[key] = value?.DeepClone();

        InjectDevComments(output, result);

        return output.ToJsonString(Options);
    }

    internal static bool Exists(Quest quest, string directory) =>
        File.Exists(Path.Combine(directory, FileName(quest)));

    internal static FileInfo Write(QuestPathResult result, string directory)
    {
        Directory.CreateDirectory(directory);
        FileInfo file = new(Path.Combine(directory, FileName(result.Quest)));
        File.WriteAllText(file.FullName, Serialize(result) + Environment.NewLine);
        return file;
    }

    private static void InjectDevComments(JsonObject output, QuestPathResult result)
    {
        if (output["QuestSequence"] is not JsonArray sequences)
            return;

        for (int i = 0; i < sequences.Count && i < result.Root.QuestSequence.Count; i++)
        {
            if (sequences[i] is not JsonObject sequenceNode)
                continue;

            QuestSequence sequence = result.Root.QuestSequence[i];
            if (sequenceNode["Steps"] is not JsonArray steps)
                continue;

            for (int j = 0; j < steps.Count && j < sequence.Steps.Count; j++)
            {
                if (steps[j] is JsonObject stepNode && result.Provenance.TryGetValue(sequence.Steps[j], out string? note))
                    stepNode["$"] = note;
            }
        }
    }

    private static void NoEmptyCollectionModifier(JsonTypeInfo typeInfo)
    {
        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            // Generic list properties (IList&lt;T&gt;) are not assignable to the non-generic ICollection, but
            // their values are - so test the declared type loosely and the value strictly.
            if (typeof(ICollection).IsAssignableFrom(property.PropertyType) || IsGenericCollection(property.PropertyType))
                property.ShouldSerialize = (_, value) => value is ICollection { Count: > 0 };
        }
    }

    private static bool IsGenericCollection(Type type) =>
        Array.Exists(type.GetInterfaces(),
            x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(ICollection<>));

    private static void AlwaysSerializeModifier(JsonTypeInfo typeInfo)
    {
        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            if (property.AttributeProvider?
                    .GetCustomAttributes(typeof(AlwaysSerializeAttribute), inherit: false)
                    .Length > 0)
            {
                property.ShouldSerialize = (_, _) => true;
            }
        }
    }

    private static void DefaultTrueModifier(JsonTypeInfo typeInfo)
    {
        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            if (property.AttributeProvider?
                    .GetCustomAttributes(typeof(DefaultTrueAttribute), inherit: false)
                    .Length > 0)
            {
                property.ShouldSerialize = (_, value) => value is false;
            }
        }
    }

    private static void SkipDefaultObjectModifier(JsonTypeInfo typeInfo)
    {
        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            if (property.AttributeProvider?
                    .GetCustomAttributes(typeof(IgnoreWhenDefaultInstanceAttribute), inherit: false)
                    .Length == 0)
                continue;

            object? defaultInstance;
            try
            {
                defaultInstance = Activator.CreateInstance(property.PropertyType);
            }
            catch (Exception ex) when (ex is MissingMethodException or MemberAccessException)
            {
                continue;
            }

            string defaultJson = JsonSerializer.Serialize(defaultInstance, Options);
            property.ShouldSerialize = (_, value) => JsonSerializer.Serialize(value, Options) != defaultJson;
        }
    }
}
