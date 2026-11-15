using Questionable.Validation;
using Xunit;

namespace Questionable.Tests.Validation;

public sealed class RouteCheckTests
{
    private static string Name(uint zone) => $"Zone {zone}";

    private static readonly Dictionary<uint, IReadOnlyList<string>> Reports = new()
    {
        { 132, ["header", "Same\tWalkPoint\t1", "Other\tWalkPoint\t2", ""] },
        { 129, ["header", "Same\tWalkPoint\t3"] },
    };

    [Fact]
    public void Combine_AddsZoneColumns()
    {
        List<string> lines = RouteCheck.Combine(Reports, Name).ToList();

        Assert.StartsWith("Zone\tZoneName\tVerdict\t", lines[0], StringComparison.Ordinal);
        Assert.Equal(
            ["129\tZone 129\tSame\tWalkPoint\t3", "132\tZone 132\tSame\tWalkPoint\t1", "132\tZone 132\tOther\tWalkPoint\t2"],
            lines.Skip(1));
    }

    [Fact]
    public void Summary_ListsEveryZone()
    {
        Dictionary<uint, int> spots = new() { { 129, 1 }, { 132, 2 }, { 204, 4 }, { 613, 9 } };
        Dictionary<uint, string> skipped = new() { { 204, "no aetheryte" } };

        List<string> lines = RouteCheck.Summary(spots, Reports, skipped, Name);

        Assert.Equal("Checked 2 of 4 zones.", lines[0]);
        Assert.Equal("Verdicts: Other 1, Same 2", lines[1]);
        Assert.Contains("  Zone 132: Other 1, Same 1", lines);
        Assert.Contains("  Zone 204: no aetheryte (4 spots)", lines);
        Assert.Contains("  Zone 613: 9 spots", lines);
    }
}
