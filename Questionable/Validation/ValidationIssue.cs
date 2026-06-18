using System.Text.Json.Serialization;
using Questionable.Model;
using Questionable.Model.Questing;
using Questionable.Model.Common.Converter;
namespace Questionable.Validation;

internal sealed record ValidationIssue
{
    public required ElementId? ElementId { get; init; }
    public required byte? Sequence { get; init; }
    public required int? Step { get; init; }
    [JsonConverter(typeof(AlliedSocietyConverter))]
    public EAlliedSociety AlliedSociety { get; init; } = EAlliedSociety.None;
    [JsonConverter(typeof(IssueTypeConverter))]
    public required EIssueType Type { get; init; }
    [JsonConverter(typeof(IssueSeverityConverter))]
    public required EIssueSeverity Severity { get; init; }
    public required string Description { get; init; }
}
