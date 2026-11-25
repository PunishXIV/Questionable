using System.Text.RegularExpressions;
using Questionable.Controller;
using Questionable.Controller.Steps.Interactions;
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
    public void EventAndStatus_KeepTextNormalization(string text, bool expected)
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
    public void MissingRows_ClassifyNothing()
    {
        var classifier = new TaskToastClassifier(null, new Regex("^Action canceled\\.$"), null);

        Assert.False(classifier.IsEventOrStatusCancellation("anything"));
        Assert.True(classifier.IsActionCancellation("Action canceled."));
    }

    [Theory]
    [InlineData("Too far away.", true)]
    [InlineData("Trop loin.", true)]
    [InlineData("Target not in line of sight.", false)]
    public void TooFarAway_MatchesAnyRow(string text, bool expected)
    {
        var classifier = new TaskToastClassifier(null, new Regex("^Action canceled\\.$"), null,
            [null, "Too far away.", "Trop loin."]);

        Assert.Equal(expected, classifier.IsTooFarAway(text));
    }

    [Fact]
    public void NoTooFarRows_MatchNothing() =>
        Assert.False(TaskRunnerFixture.CreateToasts().IsTooFarAway("Too far away."));

    [Theory]
    [InlineData(true, 0.0, true)]
    [InlineData(true, 2.5, false)]
    [InlineData(false, 0.0, false)]
    public void InteractRefusal_OnlyRightAfterTrigger(bool triggered, double secondsLater, bool expected)
    {
        DateTime at = new(2026, 11, 22, 14, 32, 50);
        Assert.Equal(expected, Interact.Executor.IsRefusal(triggered, at, at.AddSeconds(secondsLater)));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(null, "", false)]
    [InlineData("", null, false)]
    [InlineData("<icon(69)> First\r\nSecond – line ", "First\nSecond - line", true)]
    [InlineData("First  second", "First second", false)]
    [InlineData("First", "first", false)]
    public void GameTextComparison_KeepsNullAndCaseRules(string? left, string? right, bool expected) =>
        Assert.Equal(expected, GameStringComparison.AreEqual(left, right));
}
