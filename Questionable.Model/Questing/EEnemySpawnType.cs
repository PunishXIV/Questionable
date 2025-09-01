using Questionable.Model.Questing.Converter;
using System.Text.Json.Serialization;

namespace Questionable.Model.Questing;

[JsonConverter(typeof(EnemySpawnTypeConverter))]
public enum EEnemySpawnType
{
    None = 0,
    AfterInteraction,
    AfterItemUse,
    AfterAction,
    AfterEmote,
    AutoOnEnterArea,
    OverworldEnemies,
    FateEnemies,
    FinishCombatIfAny,
    QuestInterruption,
}
