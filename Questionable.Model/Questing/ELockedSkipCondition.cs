using System.Text.Json.Serialization;
using Questionable.Model.Questing.Converter;

namespace Questionable.Model.Questing;

[JsonConverter(typeof(LockedSkipConditionConverter))]
public enum ELockedSkipCondition
{
    Locked,
    Unlocked,
}
