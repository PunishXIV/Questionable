using Questionable.Model.Questing;
using Questionable.Validation;
using Questionable.Windows.JournalComponents;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class JournalTableTests
{
    private static readonly DateTime Now = new(2026, 11, 9);

    private static QuestRoot Root(bool disabled = false, string? comment = null, string? checkedOn = null) => new()
    {
        Author = ["test"],
        Disabled = disabled,
        Comment = comment,
        LastChecked = new LastChecked { Date = checkedOn },
    };

    private static EPathSupport Support(QuestRoot? root, bool removed = false, params EIssueSeverity[] issues) =>
        JournalTable.GetSupport(removed, root, issues);

    [Fact]
    public void Support_RemovedWinsOverEverything() =>
        Assert.Equal(EPathSupport.Removed, Support(Root(comment: "FATE"), removed: true));

    [Fact]
    public void Support_FateEvenWhenEnabled()
    {
        Assert.Equal(EPathSupport.Fate, Support(Root(comment: "Needs a FATE")));
        Assert.Equal(EPathSupport.Fate, Support(Root(disabled: true, comment: "FATE")));
    }

    [Fact]
    public void Support_MissingOrDisabledPath()
    {
        Assert.Equal(EPathSupport.Missing, Support(null));
        Assert.Equal(EPathSupport.Missing, Support(Root(disabled: true)));
    }

    [Fact]
    public void Support_ErrorsBeforeOtherIssues()
    {
        Assert.Equal(EPathSupport.LoadError, Support(Root(), false, EIssueSeverity.None, EIssueSeverity.Error));
        Assert.Equal(EPathSupport.Issues, Support(Root(), false, EIssueSeverity.None));
        Assert.Equal(EPathSupport.Supported, Support(Root()));
    }

    [Fact]
    public void Stale_AfterAWeekOrNeverChecked()
    {
        Assert.True(JournalTable.IsStale(Root(), Now));
        Assert.True(JournalTable.IsStale(Root(checkedOn: "2026-11-01"), Now));
        Assert.False(JournalTable.IsStale(Root(checkedOn: "2026-11-02"), Now));
        Assert.False(JournalTable.IsStale(null, Now));
    }
}
