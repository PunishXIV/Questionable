using Lumina.Excel.Sheets;

namespace Questionable.Controller.GameUi;

[RegisterSingleton]
internal sealed class TravelDestinationResolver(
    IDataManager dataManager,
    ILogger<TravelDestinationResolver> logger,
    IClientState? clientState = null)
{
    public ushort? FindTargetTerritory(QuestController.QuestProgress currentQuest)
    {
        // Manual triggers need the step count increased; automatic ones come a frame later, already counted.
        QuestSequence? sequence = currentQuest.FindSequence();
        if (sequence == null)
            return null;

        QuestStep? step = sequence.FindStep(currentQuest.Step);
        if (step != null)
        {
            logger.LogTrace("FindTargetTerritoryFromQuestStep (current): {CurrentTerritory}, {TargetTerritory}",
                step.TerritoryId,
                step.TargetTerritoryId);
        }

        if (step == null || step.TargetTerritoryId == null)
        {
            logger.LogTrace("FindTargetTerritoryFromQuestStep: Checking previous step...");
            step = sequence.FindStep(currentQuest.Step == 255 ? (sequence.Steps.Count - 1) : (currentQuest.Step - 1));

            if (step != null)
            {
                logger.LogTrace("FindTargetTerritoryFromQuestStep (previous): {CurrentTerritory}, {TargetTerritory}",
                    step.TerritoryId,
                    step.TargetTerritoryId);
            }
        }

        if (step == null || step.TargetTerritoryId == null)
        {
            logger.LogTrace("FindTargetTerritoryFromQuestStep: Not found");
            return null;
        }

        logger.LogDebug("Target territory for quest step: {TargetTerritory}", step.TargetTerritoryId);
        return step.TargetTerritoryId;
    }

    public bool TryFindWarp(ushort targetTerritoryId, string actualPrompt, [NotNullWhen(true)] out uint? warpId,
        [NotNullWhen(true)] out string? warpText)
    {
        // Island Sanctuary workaround; off once on the island, where it would answer every menu.
        if (targetTerritoryId == 1055 && clientState?.TerritoryType != 1055)
        {
            warpId = 131497;
            warpText = actualPrompt;
            return true;
        }
        IEnumerable<Warp> warps = dataManager.GetExcelSheet<Warp>()
            .Where(x => x.RowId > 0 && x.TerritoryType.RowId == targetTerritoryId);
        foreach (Warp entry in warps)
        {
            string excelName = entry.Name.WithMacrosReplaced();
            string excelQuestion = entry.Question.WithMacrosReplaced();

            if (!string.IsNullOrEmpty(excelQuestion) && GameFunctions.GameStringEquals(excelQuestion, actualPrompt))
            {
                warpId = entry.RowId;
                warpText = excelQuestion;
                return true;
            }

            if (!string.IsNullOrEmpty(excelName) && GameFunctions.GameStringEquals(excelName, actualPrompt))
            {
                warpId = entry.RowId;
                warpText = excelName;
                return true;
            }

            logger.LogDebug("Ignoring prompt '{Prompt}'", excelQuestion);
        }

        warpId = null;
        warpText = null;
        return false;
    }
}
