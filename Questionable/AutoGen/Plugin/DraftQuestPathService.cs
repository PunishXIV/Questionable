// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

using Questionable.AutoGen.Generation;
using Questionable.Model.Questing;


namespace Questionable.AutoGen;

/// <summary>
///     In-game entry point for the questpath auto-generator: turns a journal quest without a path into a draft
///     file in the user's <c>Quests</c> directory and reloads the registry so it is picked up immediately.
///     The heavy lifting lives in <see cref="QuestPathAutoGenerator"/>.
/// </summary>
internal sealed class DraftQuestPathService(
    QuestPathGeneratorFactory generatorFactory,
    Configuration configuration,
    IDalamudPluginInterface pluginInterface,
    IChatGui chatGui,
    ILogger<DraftQuestPathService> logger)
{
    /// <summary>
    ///     Generation is opt-in via <c>Advanced.AllowPathGeneration</c>, and additionally requires that drafts
    ///     can actually load: they land in the user quest directory, which the registry only reads under the
    ///     same conditions as <see cref="QuestRegistry.Reload"/> — generating a file that would never load
    ///     helps nobody.
    /// </summary>
    public bool CanGenerateDrafts => configuration.Advanced.AllowPathGeneration && UserDirectoryIsLoaded;

    /// <summary>Whether the registry reads the user <c>Quests</c> directory at all.</summary>
    public bool UserDirectoryIsLoaded => configuration.Advanced.Debug || pluginInterface.IsDev
#if DEBUG
        || true
#endif
    ;

    public void GenerateDraft(IQuestInfo questInfo)
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
