using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Questionable.Model.Questing;

namespace Questionable.Windows.Utils;

internal static class QuestStepCapture
{
    public static unsafe EInteractionType GuessInteractionType(IGameObject target)
    {
        var gameObject = (GameObject*)target.Address;
        return gameObject->NamePlateIconId switch
        {
            71201 or 71211 or 71221 or 71231 or 71341 or 71351 => EInteractionType.AcceptQuest,
            71202 or 71212 or 71222 or 71232 or 71342 or 71352 => EInteractionType.AcceptQuest,
            71205 or 71215 or 71225 or 71235 or 71345 or 71355 => EInteractionType.CompleteQuest,
            _ => EInteractionType.Interact
        };
    }

    public static QuestStep CreateStepFromTarget(IGameObject target, uint territoryType)
    {
        return new QuestStep(GuessInteractionType(target), GameFunctions.GetBaseID(target), target.Position,
            territoryType)
        {
            Fly = GameFunctions.IsFlyingUnlocked(territoryType) ? true : null
        };
    }

    public static QuestStep CreatePositionStep(Vector3 position, uint territoryType)
    {
        return new QuestStep(EInteractionType.WalkTo, null, position, territoryType)
        {
            Fly = GameFunctions.IsFlyingUnlocked(territoryType) ? true : null
        };
    }
}
