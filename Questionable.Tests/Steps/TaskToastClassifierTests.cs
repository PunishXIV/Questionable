using System.Text.RegularExpressions;
using Questionable.Controller;
using Questionable.Utils;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class TaskToastClassifierTests
{
    [Theory]
    [InlineData(" Event canceled. ", true)]
    [InlineData("<icon(69)>Event canceled.", true)]
    [InlineData("Commande impossible - état anormal.", true)]
    [InlineData("Event Canceled.", false)]
    [InlineData("Unrelated message", false)]
    public void EventAndStatusMatchingPreservesGameTextNormalization(string text, bool expected)
    {
        var classifier = new TaskToastClassifier("Event canceled.", new Regex("^Action canceled\\.$"),
            "<italic>Commande impossible – état anormal.</italic>");

        Assert.Equal(expected, classifier.IsEventOrStatusCancellation(text));
    }

    [Theory]
    [InlineData("Action canceled. You are under attack.", true)]
    [InlineData(" Action canceled. You are under attack. ", false)]
    [InlineData("Event canceled.", false)]
    public void ActionCancellationKeepsRegexSemantics(string text, bool expected) =>
        Assert.Equal(expected, TaskRunnerFixture.CreateToasts().IsActionCancellation(text));

    [Fact]
    public void MissingEventAndStatusRowsDoNotClassifyUnrelatedToasts()
    {
        var classifier = new TaskToastClassifier(null, new Regex("^Action canceled\\.$"), null);

        Assert.False(classifier.IsEventOrStatusCancellation("anything"));
        Assert.True(classifier.IsActionCancellation("Action canceled."));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(null, "", false)]
    [InlineData("", null, false)]
    [InlineData("<icon(69)> First\r\nSecond – line ", "First\nSecond - line", true)]
    [InlineData("First  second", "First second", false)]
    [InlineData("First", "first", false)]
    public void SharedGameTextComparisonRetainsNullWhitespaceAndCaseRules(string? left, string? right, bool expected) =>
        Assert.Equal(expected, GameStringComparison.AreEqual(left, right));
}
