using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Dalamud.Plugin.Services;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model.Gathering;
using Questionable.Model.Questing;
using Questionable.Utils;

namespace Questionable.Controller.GameUi;

/// <summary>
///     Resolves travel destinations for interaction handlers: the target territory of the current
///     quest step, and the warp entry matching a given prompt. Shared by the dialogue-choice and
///     yes/no interaction handlers.
/// </summary>
internal sealed class TravelDestinationResolver(
    IClientState clientState,
    GatheringPointRegistry gatheringPointRegistry,
    IDataManager dataManager,
    ILogger<TravelDestinationResolver> logger)
{
    public unsafe ushort? FindTargetTerritoryFromQuestStep(QuestController.QuestProgress currentQuest)
    {
        // this can be triggered either manually (in which case we should increase the step counter), or automatically
        // (in which case it is ~1 frame later, and the step counter has already been increased)
        QuestSequence? sequence = currentQuest.Quest.FindSequence(currentQuest.Sequence);
        if (sequence == null)
            return null;

        QuestStep? step = sequence.FindStep(currentQuest.Step);
        if (step != null)
        {
            logger.LogTrace("FindTargetTerritoryFromQuestStep (current): {CurrentTerritory}, {TargetTerritory}",
                step.TerritoryId,
                step.TargetTerritoryId);
        }

        if (step != null && (step.TerritoryId != clientState.TerritoryType || step.TargetTerritoryId == null) &&
            step.InteractionType == EInteractionType.Gather)
        {
            if (gatheringPointRegistry.TryGetGatheringPointId(step.ItemsToGather[0].ItemId,
                    (Job?)PlayerState.Instance()->CurrentClassJobId ?? Job.ADV,
                    out GatheringPointId? gatheringPointId) &&
                gatheringPointRegistry.TryGetGatheringPoint(gatheringPointId, out GatheringRoot? root))
            {
                foreach (QuestStep gatheringStep in root.Steps)
                {
                    if (gatheringStep.TerritoryId == clientState.TerritoryType && gatheringStep.TargetTerritoryId != null)
                    {
                        logger.LogTrace(
                            "FindTargetTerritoryFromQuestStep (gathering): {CurrentTerritory}, {TargetTerritory}",
                            gatheringStep.TerritoryId,
                            gatheringStep.TargetTerritoryId);
                        return gatheringStep.TargetTerritoryId;
                    }
                }
            }
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
        IEnumerable<Warp> warps = dataManager.GetExcelSheet<Warp>()
            .Where(x => x.RowId > 0 && x.TerritoryType.RowId == targetTerritoryId);
        foreach (Warp entry in warps)
        {
            string excelName = entry.Name.WithCertainMacroCodeReplacements();
            string excelQuestion = entry.Question.WithCertainMacroCodeReplacements();

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
