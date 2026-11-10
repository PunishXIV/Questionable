using System.Text.Json.Serialization;
namespace Questionable.Validation;

internal sealed record ValidationIssue
{
    public required ElementId? ElementId { get; init; }
    public required byte? Sequence { get; init; }
    public required int? Step { get; init; }
    public uint? TerritoryId { get; set; }
    [JsonConverter(typeof(IssueTypeConverter))]
    public required EIssueType Type { get; init; }
    [JsonConverter(typeof(IssueSeverityConverter))]
    public required EIssueSeverity Severity { get; init; }
    public required string Description { get; init; }
}
