using Questionable.Model.Questing.Converter;
using System.Text.Json.Serialization;

namespace Questionable.Model.Questing;

[JsonConverter(typeof(LockedSkipConditionConverter))]
public enum ELockedSkipCondition
{
    Locked,
    Unlocked,
}
