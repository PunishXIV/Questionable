using System.Collections.Generic;
using System.Text.Json.Serialization;
using Questionable.Model.Common.Converter;
using Questionable.Model.Questing.Converter;

namespace Questionable.Model.Questing;

public sealed class QuestRoot
{
    [JsonConverter(typeof(StringListOrValueConverter))]
    public List<string> Author { get; set; } = new();

    /// <summary>
    /// This is only relevant for release builds.
    /// </summary>
    public bool Disabled { get; set; }

    public bool Interruptible { get; set; } = true;
    public string? Comment { get; set; }
    public List<QuestSequence> QuestSequence { get; set; } = new();
}
