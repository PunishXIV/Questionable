using Questionable.Model;
using Questionable.Model.Questing;

namespace Questionable.Validation;

internal sealed record ValidationIssue
{
    public required ElementId? ElementId { get; init; }
    public required byte? Sequence { get; init; }
    public required int? Step { get; init; }
    public EAlliedSociety AlliedSociety { get; init; } = EAlliedSociety.None;
    public required EIssueType Type { get; init; }
    public required EIssueSeverity Severity { get; init; }
    public required string Description { get; init; }
}
