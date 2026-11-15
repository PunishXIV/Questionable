using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Questionable.Model.Common;
using Questionable.Model.Common.Converter;
namespace Questionable.Model.Questing;

public sealed class QuestRoot
{
    [JsonConverter(typeof(StringListOrValueConverter))]
    public List<string> Author { get; set; } = new();

    // Only relevant for release builds.
    public bool Disabled { get; set; }

    [DefaultTrue]
    public bool Interruptible { get; set; } = true;
    public string? Comment { get; set; }
    public bool? IsSeasonalQuest { get; set; }
    public DateTime? SeasonalQuestExpiry { get; set; }
    public List<QuestSequence> QuestSequence { get; set; } = new();
    [IgnoreWhenDefaultInstance]
    public LastChecked LastChecked { get; set; } = new();
}
