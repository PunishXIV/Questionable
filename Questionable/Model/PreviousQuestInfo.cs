using Questionable.Model.Questing;

namespace Questionable.Model;

internal sealed record PreviousQuestInfo(QuestId QuestId, byte Sequence = 0);
