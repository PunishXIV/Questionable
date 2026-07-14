using Questionable.Model.Questing;
namespace Questionable.Domain;

internal sealed record PreviousQuestInfo(QuestId QuestId, byte Sequence = 0);
