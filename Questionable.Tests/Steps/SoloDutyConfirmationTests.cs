using Questionable.Controller;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;
using Confirmation = Questionable.Controller.GameUi.YesNoChoiceHandler.SoloDutyConfirmation;

namespace Questionable.Tests.Steps;

public sealed class SoloDutyConfirmationTests
{
    private const string Prompt = "Duty calls. Commence battle for 'In from the Cold'?";
    private static readonly DateTime RequestedAt = DateTime.UnixEpoch;

    [Fact]
    public void ReadyMatchingPrompt_ConfirmsOnceAfterInitialDifficultyDelay()
    {
        var quest = CreateProgress();
        var confirmation = new Confirmation(quest, Prompt, RequestedAt);

        Assert.Equal(Confirmation.Result.Wait,
            confirmation.Check(RequestedAt.AddSeconds(1), quest, Prompt, ready: true, loading: false));
        Assert.Equal(Confirmation.Result.Confirm,
            confirmation.Check(RequestedAt.AddSeconds(2), quest, Prompt, ready: true, loading: false));
        Assert.Equal(Confirmation.Result.Cancel,
            confirmation.Check(RequestedAt.AddSeconds(3), quest, Prompt, ready: true, loading: false));
    }

    [Fact]
    public void DialogDelay_KeepsFallbackUntilReady()
    {
        var quest = CreateProgress();
        var confirmation = new Confirmation(quest, Prompt, RequestedAt);

        for (int second = 2; second < 60; ++second)
            Assert.Equal(Confirmation.Result.Wait,
                confirmation.Check(RequestedAt.AddSeconds(second), quest, Prompt, ready: false, loading: false));

        Assert.Equal(Confirmation.Result.Confirm,
            confirmation.Check(RequestedAt.AddSeconds(60), quest, Prompt, ready: true, loading: false));
    }

    [Fact]
    public void Loading_DelaysEvenAReadyMatchingPrompt()
    {
        var quest = CreateProgress();
        var confirmation = new Confirmation(quest, Prompt, RequestedAt);

        Assert.Equal(Confirmation.Result.Wait,
            confirmation.Check(RequestedAt.AddSeconds(2), quest, Prompt, ready: true, loading: true));
        Assert.Equal(Confirmation.Result.Confirm,
            confirmation.Check(RequestedAt.AddSeconds(3), quest, Prompt, ready: true, loading: false));
    }

    [Fact]
    public void UnrelatedPrompt_CancelsInsteadOfAcceptingItOrReusingTheRequest()
    {
        var quest = CreateProgress();
        var confirmation = new Confirmation(quest, Prompt, RequestedAt);

        Assert.Equal(Confirmation.Result.Cancel,
            confirmation.Check(RequestedAt.AddSeconds(2), quest, "Accept this party invitation?", ready: true, loading: false));
        Assert.Equal(Confirmation.Result.Cancel,
            confirmation.Check(RequestedAt.AddSeconds(3), quest, Prompt, ready: true, loading: false));
    }

    [Fact]
    public void MissingPrompt_IsNotEvidenceOfReadiness()
    {
        var quest = CreateProgress();
        var confirmation = new Confirmation(quest, Prompt, RequestedAt);

        Assert.Equal(Confirmation.Result.Wait,
            confirmation.Check(RequestedAt.AddSeconds(2), quest, null, ready: true, loading: false));
        Assert.Equal(Confirmation.Result.Confirm,
            confirmation.Check(RequestedAt.AddSeconds(3), quest, Prompt, ready: true, loading: false));
    }

    [Theory]
    [InlineData("different quest")]
    [InlineData("same quest restarted")]
    [InlineData("next sequence")]
    [InlineData("next step")]
    [InlineData("same step restarted")]
    [InlineData("no quest")]
    public void ChangedQuestOrStep_CancelsTheOldConfirmation(string change)
    {
        QuestController.QuestProgress? quest = CreateProgress();
        var confirmation = new Confirmation(quest, Prompt, RequestedAt);
        switch (change)
        {
            case "different quest":
                quest = new QuestController.QuestProgress(QuestTestData.CreateQuest(new QuestId(1)));
                break;
            case "same quest restarted":
                quest = new QuestController.QuestProgress(quest.Quest, quest.Sequence, quest.Step);
                break;
            case "next sequence":
                quest.SetSequence(2);
                break;
            case "next step":
                quest.SetStep(1);
                break;
            case "same step restarted":
                quest.SetStep(quest.Step);
                break;
            case "no quest":
                quest = null;
                break;
        }

        Assert.Equal(Confirmation.Result.Cancel,
            confirmation.Check(RequestedAt.AddSeconds(2), quest, Prompt, ready: true, loading: false));
    }

    [Fact]
    public void ExpiredConfirmation_DoesNotClickALatePrompt()
    {
        var quest = CreateProgress();
        var confirmation = new Confirmation(quest, Prompt, RequestedAt);

        Assert.Equal(Confirmation.Result.Wait,
            confirmation.Check(RequestedAt.AddSeconds(119), quest, Prompt, ready: false, loading: false));
        Assert.Equal(Confirmation.Result.Cancel,
            confirmation.Check(RequestedAt.AddMinutes(2), quest, Prompt, ready: true, loading: false));
    }

    [Fact]
    public void RepeatedChecks_AreThrottledWhileWaitingForReadiness()
    {
        var quest = CreateProgress();
        var confirmation = new Confirmation(quest, Prompt, RequestedAt);

        Assert.Equal(Confirmation.Result.Wait,
            confirmation.Check(RequestedAt.AddSeconds(2), quest, Prompt, ready: false, loading: false));
        Assert.Equal(Confirmation.Result.Wait,
            confirmation.Check(RequestedAt.AddSeconds(2.1), quest, Prompt, ready: true, loading: false));
        Assert.Equal(Confirmation.Result.Confirm,
            confirmation.Check(RequestedAt.AddSeconds(2.5), quest, Prompt, ready: true, loading: false));
    }

    private static QuestController.QuestProgress CreateProgress()
    {
        var (quest, _, _) = QuestTestData.FactoryContext(new QuestId(4394), 1,
            new QuestStep { InteractionType = EInteractionType.SinglePlayerDuty });
        return new QuestController.QuestProgress(quest, 1);
    }
}
