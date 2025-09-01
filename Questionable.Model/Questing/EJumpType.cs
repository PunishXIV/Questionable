using Questionable.Model.Questing.Converter;
using System.Text.Json.Serialization;

namespace Questionable.Model.Questing;

[JsonConverter(typeof(JumpTypeConverter))]
public enum EJumpType
{
    SingleJump,
    RepeatedJumps,
}
