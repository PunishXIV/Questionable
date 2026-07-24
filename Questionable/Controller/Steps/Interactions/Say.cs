using Questionable.Controller.Steps.Common;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Interactions;

internal static class Say
{
    internal sealed class Factory(ExcelFunctions excelFunctions) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType is EInteractionType.AcceptQuest or EInteractionType.CompleteQuest)
            {
                if (step.ChatMessage == null)
                    return [];
            }
            else if (step.InteractionType != EInteractionType.Say)
                return [];


            ArgumentNullException.ThrowIfNull(step.ChatMessage);

            string? excelString =
                excelFunctions.GetDialogueText(quest, step.ChatMessage.ExcelSheet, step.ChatMessage.Key, isRegex: false)
                    .GetString();
            ArgumentNullException.ThrowIfNull(excelString);

            Mount.UnmountTask unmount = new();
            Task task = new(excelString);
            return [unmount, task];
        }
    }

    internal sealed record Task(string ChatMessage) : ITask
    {
        public override string ToString() => $"Say({ChatMessage})";
    }

    internal sealed class UseChat(ChatFunctions chatFunctions) : AbstractDelayedTaskExecutor<Task>
    {
        protected override bool StartInternal()
        {
            chatFunctions.ExecuteCommand($"/say {Task.ChatMessage}");
            return true;
        }

        public override bool ShouldInterruptOnDamage() => true;
    }
}
