using System.Text.Json.Serialization;
using Questionable.Model.Questing.Converter;

namespace Questionable.Model.Questing;

[JsonConverter(typeof(JumpTypeConverter))]
public enum EJumpType
{
    SingleJump,
    RepeatedJumps,
}
