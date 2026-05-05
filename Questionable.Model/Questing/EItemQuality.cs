using System.Text.Json.Serialization;
namespace Questionable.Model.Questing;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EItemQuality
{
    Any,
    NQ,
    HQ
}
