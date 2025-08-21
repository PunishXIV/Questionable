using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Questionable.Data;
using Questionable.External;
using Questionable.Model;
using Questionable.Model.Questing;

namespace Questionable.Controller.Steps.Common;

internal static class SendNotification
{
    internal sealed class Factory(
        AutomatonIpc automatonIpc,
        AutoDutyIpc autoDutyIpc,
        BossModIpc bossModIpc,
        TerritoryData territoryData) : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            return step.InteractionType switch
            {
                EInteractionType.Snipe when !automatonIpc.IsAutoSnipeEnabled =>
                    new Task(step.InteractionType, step.Comment),
                EInteractionType.Duty when !autoDutyIpc.IsConfiguredToRunContent(step.DutyOptions) =>
                    new Task(step.InteractionType, step.DutyOptions?.ContentFinderConditionId is {} contentFinderConditionId
                        ? territoryData.GetContentFinderCondition(contentFinderConditionId)?.Name
                        : step.Comment),
                EInteractionType.SinglePlayerDuty when !bossModIpc.IsConfiguredToRunSoloInstance(quest.Id, step.SinglePlayerDutyOptions) =>
                    new Task(step.InteractionType, quest.Info.Name),
                _ => null,
            };
        }
    }

    internal sealed record Task(EInteractionType InteractionType, string? Comment) : ITask
    {
        public override string ToString() => "SendNotification";
    }

    internal sealed class Executor(
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
                EInteractionType.Instruction or EInteractionType.WaitForManualProgress or EInteractionType.Snipe =>
                    "Manual interaction required",
                _ => $"{Task.InteractionType}",
            };

            if (!string.IsNullOrEmpty(Task.Comment))
                text += $" - {Task.Comment}";

            if (configuration.Notifications.ChatType != XivChatType.None)
            {
                var message = configuration.Notifications.ChatType switch
                {
                    XivChatType.Say
                        or XivChatType.Shout
                        or XivChatType.TellOutgoing
                        or XivChatType.TellIncoming
                        or XivChatType.Party
                        or XivChatType.Alliance
                        or (>= XivChatType.Ls1 and <= XivChatType.Ls8)
                        or XivChatType.FreeCompany
                        or XivChatType.NoviceNetwork
                        or XivChatType.Yell
                        or XivChatType.CrossParty
                        or XivChatType.PvPTeam
                        or XivChatType.CrossLinkShell1
                        or XivChatType.NPCDialogue
                        or XivChatType.NPCDialogueAnnouncements
                        or (>= XivChatType.CrossLinkShell2 and <= XivChatType.CrossLinkShell8)
                        => new XivChatEntry
                        {
                            Message = text,
                            Type = configuration.Notifications.ChatType,
                            Name = new SeStringBuilder()
                                .AddUiForeground(CommandHandler.MessageTag, CommandHandler.TagColor)
                                .Build(),
                        },
                    _ => new XivChatEntry
                    {
                        Message = new SeStringBuilder()
                            .AddUiForeground($"[{CommandHandler.MessageTag}] ", CommandHandler.TagColor)
                            .Append(text)
                            .Build(),
                        Type = configuration.Notifications.ChatType,
                    }
                };
                chatGui.Print(message);
            }

            notificationMasterIpc.Notify(text);
            return true;
        }

        public override ETaskResult Update() => ETaskResult.TaskComplete;

        public override bool ShouldInterruptOnDamage() => false;
    }
}
