using System.Text.Json.Serialization;
using Questionable.Model.Questing.Converter;

namespace Questionable.Model.Questing;

[JsonConverter(typeof(QuestWorkModeConverter))]
public enum EQuestWorkMode
{
    Bitwise,
    Exact,
}
