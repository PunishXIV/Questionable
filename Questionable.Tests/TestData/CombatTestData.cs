// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu

using Questionable.Controller;
using Questionable.Model.Questing;

namespace Questionable.Tests.TestData;

internal static class CombatTestData
{
    public static CombatController.CombatData Create(
        EEnemySpawnType spawnType = EEnemySpawnType.AfterInteraction, CombatItemUse? itemUse = null) => new()
    {
        ElementId = null,
        Sequence = 0,
        CompletionQuestVariablesFlags = [],
        SpawnType = spawnType,
        KillEnemyDataIds = [],
        ComplexCombatDatas = [],
        CombatItemUse = itemUse,
    };
}
