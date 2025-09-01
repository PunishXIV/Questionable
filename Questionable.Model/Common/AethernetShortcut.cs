using Questionable.Model.Questing.Converter;
using System.Text.Json.Serialization;

namespace Questionable.Model.Common;

[JsonConverter(typeof(AethernetShortcutConverter))]
public sealed class AethernetShortcut
{
    public EAetheryteLocation From { get; set; }
    public EAetheryteLocation To { get; set; }
}
