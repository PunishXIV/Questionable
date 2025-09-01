using Questionable.Model.Questing.Converter;
using System.Text.Json.Serialization;

namespace Questionable.Model.Questing;

[JsonConverter(typeof(QuestWorkModeConverter))]
public enum EQuestWorkMode
{
    Bitwise,
    Exact,
}
