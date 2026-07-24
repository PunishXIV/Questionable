using System.Collections;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Questionable.Model.Common;

namespace Questionable.Utils;

internal static class JsonOptions
{
    internal static readonly JsonSerializerOptions Default = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { NoEmptyCollectionModifier, AlwaysSerializeAttributeModifier, DefaultTrueModifier, IgnoreWhenDefaultInstanceModifier }
        }
    };
    internal static void NoEmptyCollectionModifier(JsonTypeInfo typeInfo)
    {
        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            if (typeof(ICollection).IsAssignableFrom(property.PropertyType))
                property.ShouldSerialize = (_, val) => val is ICollection { Count: > 0 };
        }
    }
    internal static void AlwaysSerializeAttributeModifier(JsonTypeInfo typeInfo)
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
    internal static void DefaultTrueModifier(JsonTypeInfo typeInfo)
    {
        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            if (property.AttributeProvider?
                    .GetCustomAttributes(typeof(DefaultTrueAttribute), inherit: false)
                    .Length > 0)
            {
                property.ShouldSerialize = (_, val) => val is false;
            }
        }
    }
    internal static void IgnoreWhenDefaultInstanceModifier(JsonTypeInfo typeInfo)
    {
        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            if (property.AttributeProvider?
                    .GetCustomAttributes(typeof(IgnoreWhenDefaultInstanceAttribute), inherit: false)
                    .Length == 0)
                continue;

            object? defaultInstance = null;
            try { defaultInstance = Activator.CreateInstance(property.PropertyType); }
            catch { continue; }

            // Serialize the default once so we can compare JSON output
            string defaultJson = JsonSerializer.Serialize(defaultInstance, Default);

            property.ShouldSerialize = (_, val) =>
                JsonSerializer.Serialize(val, Default) != defaultJson;
        }
    }
}
