using System.Text.Json.Serialization;
using Questionable.Model.Common;
using Questionable.Model.Questing.Converter;
namespace Questionable.Model.Questing;

public sealed class DialogueChoice
{
    [JsonConverter(typeof(DialogueChoiceTypeConverter))]
    public EDialogChoiceType Type { get; set; }
    public string? ExcelSheet { get; set; }

    [JsonConverter(typeof(ExcelRefConverter))]
    public ExcelRef? Prompt { get; set; }

    [DefaultTrue]
    public bool Yes { get; set; } = true;

    [JsonConverter(typeof(ExcelRefConverter))]
    public ExcelRef? Answer { get; set; }
    public bool PromptIsRegularExpression { get; set; }
    public bool AnswerIsRegularExpression { get; set; }

    // If set, the choice only applies while targeting this object.
    public uint? DataId { get; set; }

    // Used for In from the Cold.
    public string? SpecialCondition { get; set; }
}
