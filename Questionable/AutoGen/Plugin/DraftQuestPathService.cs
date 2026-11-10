// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

using Questionable.AutoGen.Generation;
namespace Questionable.AutoGen.Plugin;

[RegisterSingleton]
internal sealed class DraftQuestPathService(
    QuestPathGeneratorFactory generatorFactory,
    Configuration configuration,
    IDalamudPluginInterface pluginInterface,
    IChatGui chatGui,
    ILogger<DraftQuestPathService> logger)
{
    internal bool CanGenerateDrafts => configuration.Advanced.AllowPathGeneration && UserDirectoryIsLoaded;

    internal bool UserDirectoryIsLoaded => configuration.Advanced.Debug || pluginInterface.IsDev
#if DEBUG
        || true
#endif
    ;

    internal void GenerateDraft(IQuestInfo questInfo)
    {
        try
        {
            if (questInfo.QuestId is not QuestId questId)
            {
                chatGui.PrintError(_L("Only normal quests can be auto-generated."),
                    CommandHandler.MessageTag, CommandHandler.TagColor);
                return;
            }

            QuestPathResult? result =
                generatorFactory.GenerateById(questId.Value, configuration.General.DisplayName);
            if (result == null)
            {
                chatGui.PrintError(_LF("Could not find quest {0} in the game data.", questId),
                    CommandHandler.MessageTag, CommandHandler.TagColor);
                return;
            }

            string? userQuestsDirectory = QuestRegistry.GetFullPath(questInfo, withFilename: false);
            if (userQuestsDirectory != null)
            {
                FileInfo file = QuestPathWriter.Write(result, userQuestsDirectory);

                chatGui.Print(
                    _LF("Generated draft path: {0} ({1} sequences). Review it before trusting it.",
                        file.Name, result.Root.QuestSequence.Count),
                    CommandHandler.MessageTag, CommandHandler.TagColor);
                foreach (string note in result.Notes)
                    chatGui.Print($"- {note}", CommandHandler.MessageTag, CommandHandler.TagColor);

                chatGui.PrintError(
                    _L("Experimental: expect wrong targets and stalls. Watch the quest while it runs - do not go AFK."),
                    CommandHandler.MessageTag, CommandHandler.TagColor);
            }
        }
#pragma warning disable CA1031 // a failed draft should never take the UI down with it
        catch (Exception e)
        {
            logger.LogError(e, "Unable to generate draft quest path for {QuestId}", questInfo.QuestId);
            chatGui.PrintError(_L("Unable to generate a draft path, check /xllog for details."),
                CommandHandler.MessageTag, CommandHandler.TagColor);
        }
#pragma warning restore CA1031
    }
}
