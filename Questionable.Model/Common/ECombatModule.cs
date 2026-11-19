using System.Text.Json.Serialization;
using Questionable.Model.Common.Converter;

namespace Questionable.Model.Common;

[JsonConverter(typeof(CombatModuleConverter))]
public enum ECombatModule
{
    None,
    BossMod,
    WrathCombo,
    RotationSolverReborn
}

public sealed class CombatModuleConverter() : EnumConverter<ECombatModule>();