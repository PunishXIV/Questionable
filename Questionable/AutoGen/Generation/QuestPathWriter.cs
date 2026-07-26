// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

using System.Collections;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Lumina.Excel.Sheets;
using Quest = Lumina.Excel.Sheets.Quest;
using Questionable.Model.Common;
using Questionable.Model.Questing;

namespace Questionable.AutoGen.Generation;

/// <summary>Serializes a generated path the same way the plugin's own editor does.</summary>
public static class QuestPathWriter
{
    private const string SchemaUrl = "https://qstxiv.github.io/schema/quest-v1.json";

    /// <summary>Mirrors <c>Questionable.Utils.JsonOptions.Default</c>, which the checked-in paths are written with.</summary>
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
                AlwaysSerializeAttributeModifier,
                DefaultTrueModifier,
                IgnoreWhenDefaultInstanceModifier
            }
        }
    };

    public static string FileName(Quest quest) =>
        $"{quest.RowId & 0xFFFF}_{SimplifyName(QuestGameData.QuestName(quest))}.json";

    /// <summary>Same character stripping <c>QuestInfo.SimplifiedName</c> applies before using a name as a filename.</summary>
    private static string SimplifyName(string name)
    {
        foreach (char invalid in new[] { '.', '*', '"', '/', '\\', '<', '>', '|', ':', '?' })
            name = name.Replace(invalid.ToString(), string.Empty, StringComparison.Ordinal);

        return name.Trim();
    }

    public static string Serialize(QuestPathResult result)
    {
        JsonObject serialized = (JsonObject)JsonSerializer.SerializeToNode(result.Root, Options)!;

        JsonObject output = new() { ["$schema"] = SchemaUrl };
        foreach ((string key, JsonNode? value) in serialized)
            output[key] = value?.DeepClone();

        InjectDevComments(output, result);

        return output.ToJsonString(Options);
    }

    /// <summary>Whether a path for this quest is already on disk, and so should be left alone.</summary>
    public static bool Exists(Quest quest, string directory) =>
        File.Exists(Path.Combine(directory, FileName(quest)));

    public static FileInfo Write(QuestPathResult result, string directory)
    {
        Directory.CreateDirectory(directory);
        FileInfo file = new(Path.Combine(directory, FileName(result.Quest)));
        File.WriteAllText(file.FullName, Serialize(result) + Environment.NewLine);
        return file;
    }

    /// <summary>
    ///     Adds the <c>$</c> dev comment to each step. It is not part of the C# model — only of the JSON schema —
    ///     so it has to go in after serialization, matched up positionally with the sequences it came from.
    /// </summary>
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

    private static void AlwaysSerializeAttributeModifier(JsonTypeInfo typeInfo)
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

    private static void IgnoreWhenDefaultInstanceModifier(JsonTypeInfo typeInfo)
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
