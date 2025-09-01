using Questionable.Model.Common.Converter;
using System.Collections.Generic;

namespace Questionable.Model.Questing.Converter;

public sealed class EnemySpawnTypeConverter() : EnumConverter<EEnemySpawnType>(Values)
{
    private static readonly Dictionary<EEnemySpawnType, string> Values = new()
    {
        { EEnemySpawnType.AfterInteraction, "AfterInteraction" },
        { EEnemySpawnType.AfterItemUse, "AfterItemUse" },
        { EEnemySpawnType.AfterAction, "AfterAction" },
        { EEnemySpawnType.AfterEmote, "AfterEmote" },
        { EEnemySpawnType.AutoOnEnterArea, "AutoOnEnterArea" },
        { EEnemySpawnType.OverworldEnemies, "OverworldEnemies" },
        { EEnemySpawnType.FateEnemies, "FateEnemies" },
        { EEnemySpawnType.FinishCombatIfAny, "FinishCombatIfAny" }
    };
}
