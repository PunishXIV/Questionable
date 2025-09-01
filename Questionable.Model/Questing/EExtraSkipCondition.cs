using Questionable.Model.Questing.Converter;
using System.Text.Json.Serialization;

namespace Questionable.Model.Questing;

[JsonConverter(typeof(SkipConditionConverter))]
public enum EExtraSkipCondition
{
    None,
    WakingSandsMainArea,
    WakingSandsSolar,
    RisingStonesSolar,

    /// <summary>
    /// Location for ROG quests in Limsa Lominsa; located far underneath the actual lower decks.
    /// </summary>
    RoguesGuild,
    NotRoguesGuild,

    /// <summary>
    /// Location for NIN quests in Eastern La Noscea; located far underneath the actual zone.
    /// </summary>
    DockStorehouse,
}
