using Questionable.Controller.GameUi;
using Questionable.Model.Questing;
using Xunit;
using Recovery = Questionable.Controller.GameUi.InteractionUiController.StartupDialogueRecovery;

namespace Questionable.Tests.Steps;

public sealed class StartupDialogueRecoveryTests
{
    [Fact]
    public void QuestThenDialog_BothSeenBeforeStart()
    {
        var start = DateTime.UnixEpoch;
        var recovery = new Recovery(start);
        Assert.False(recovery.ShouldCheck(start, hasQuest: false, hasTasks: false, executorStarted: false, externalOperation: false));

        int checks = 0;
        bool answered = false;
        for (int tick = 0; tick <= 3; ++tick)
        {
            DateTime now = start.AddMilliseconds(152 + tick * 500);
            if (recovery.ShouldCheck(now, hasQuest: true, hasTasks: true, executorStarted: false, externalOperation: false))
            {
                ++checks;
                // The reported supper prompt appears 1.45 seconds after Start, after quest discovery.
                answered |= now >= start.AddMilliseconds(1450);
            }
        }

        Assert.Equal(4, checks);
        Assert.True(answered);
        Assert.False(recovery.Finished);
    }

    [Fact]
    public void RecoveryWindow_Expires()
    {
        var start = DateTime.UnixEpoch;
        var recovery = new Recovery(start);
        Assert.False(recovery.ShouldCheck(start.AddMinutes(2), hasQuest: true, hasTasks: true, executorStarted: false, externalOperation: false));
        Assert.True(recovery.Finished);
    }

    [Fact]
    public void SequenceRecovery_OnlyYesNoPrompts()
    {
        var supper = new DialogueChoice { Type = EDialogChoiceType.YesNo, Prompt = ExcelRef.FromKey("SUPPER"), Yes = true };
        var sequence = new QuestSequence
        {
            Sequence = 1,
            Steps =
            [
                new QuestStep { InteractionType = EInteractionType.AcceptQuest },
                new QuestStep
                {
                    InteractionType = EInteractionType.SinglePlayerDuty,
                    DialogueChoices =
                    [
                        supper,
                        new DialogueChoice { Type = EDialogChoiceType.YesNo, Yes = true },
                        new DialogueChoice { Type = EDialogChoiceType.List, Prompt = ExcelRef.FromRowId(254) }
                    ]
                }
            ]
        };

        Assert.Same(supper, Assert.Single(YesNoChoiceHandler.ExplicitYesNoChoices(sequence)));
    }
}
