using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Common;

internal static class SendNotification
{
    internal sealed class Factory
    (
        AutomatonIpc automatonIpc,
        AutoDutyIpc autoDutyIpc,
        IAutoHookIpc autoHookIpc,
        BossModIpc bossModIpc,
        TerritoryData territoryData) : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            return step.InteractionType switch
            {
                EInteractionType.Snipe when !automatonIpc.IsAutoSnipeEnabled =>
                    new(step.InteractionType, step.Comment),
                EInteractionType.Duty when !autoDutyIpc.IsConfiguredToRunContent(step.DutyOptions) =>
                    new(step.InteractionType, step.DutyOptions?.ContentFinderConditionId is { } contentFinderConditionId
                        ? territoryData.GetContentFinderCondition(contentFinderConditionId)?.Name
                        : step.Comment),
                EInteractionType.SinglePlayerDuty when !bossModIpc.IsConfiguredToRunSoloInstance(quest.Id, step.SinglePlayerDutyOptions) =>
                    new Task(step.InteractionType, quest.Info.Name),
                EInteractionType.Fish when !autoHookIpc.IsAvailable() =>
                    new(step.InteractionType, step.Comment ?? "AutoHook plugin is required for automatic fishing"),
                var _ => null
            };
        }
    }

    internal sealed record Task(EInteractionType InteractionType, string? Comment) : ITask
    {
        public override string ToString() => "SendNotification";
    }

    internal sealed class Executor
    (
        NotificationMasterIpc notificationMasterIpc,
        IChatGui chatGui,
        Configuration configuration) : TaskExecutor<Task>
    {
        protected override bool Start()
        {
            if (!configuration.Notifications.Enabled)
                return false;

            string text = Task.InteractionType switch
            {
                EInteractionType.Duty => "Duty",
                EInteractionType.SinglePlayerDuty => "Single player duty",
                EInteractionType.Instruction or EInteractionType.WaitForManualProgress or EInteractionType.Snipe
                    or EInteractionType.Fish =>
                    "Manual interaction required",
                var _ => $"{Task.InteractionType}"
            };

            if (!string.IsNullOrEmpty(Task.Comment))
                text += $" - {Task.Comment}";

            if (configuration.Notifications.ChatType != XivChatType.None)
                chatGui.Print(CreateChatMessage(text, configuration.Notifications.ChatType));

            notificationMasterIpc.Notify(text);
            return true;
        }

        private static XivChatEntry CreateChatMessage(string text, XivChatType chatType)
        {
            // channels that show a sender get the tag as the sender, the others get it in front of the message
            if (HasSender(chatType))
            {
                return new()
                {
                    Message = text,
                    Type = chatType,
                    Name = new SeStringBuilder()
                        .AddUiForeground(CommandHandler.MessageTag, CommandHandler.TagColor)
                        .Build()
                };
            }

            return new()
            {
                Message = new SeStringBuilder()
                    .AddUiForeground($"[{CommandHandler.MessageTag}] ", CommandHandler.TagColor)
                    .Append(text)
                    .Build(),
                Type = chatType
            };
        }

        private static bool HasSender(XivChatType chatType) =>
            chatType is XivChatType.Say
                or XivChatType.Shout
                or XivChatType.TellOutgoing
                or XivChatType.TellIncoming
                or XivChatType.Party
                or XivChatType.Alliance
                or >= XivChatType.Ls1 and <= XivChatType.Ls8
                or XivChatType.FreeCompany
                or XivChatType.NoviceNetwork
                or XivChatType.Yell
                or XivChatType.CrossParty
                or XivChatType.PvPTeam
                or XivChatType.CrossLinkShell1
                or XivChatType.NPCDialogue
                or XivChatType.NPCDialogueAnnouncements
                or >= XivChatType.CrossLinkShell2 and <= XivChatType.CrossLinkShell8;

        public override ETaskResult Update() => ETaskResult.TaskComplete;

        public override bool ShouldInterruptOnDamage() => false;
    }
}
