using System.Text.Json.Serialization;
using Questionable.Model.Questing.Converter;
namespace Questionable.Model.Questing;

[JsonConverter(typeof(SkipConditionConverter))]
public enum EExtraSkipCondition
{
    None,
    WakingSandsMainArea,
    WakingSandsSolar,
    RisingStonesSolar,

    /// <summary>
    ///     Location for ROG quests in Limsa Lominsa; located far underneath the actual lower decks.
    /// </summary>
    RoguesGuild,
    NotRoguesGuild,

    /// <summary>
    ///     Location for NIN quests in Eastern La Noscea; located far underneath the actual zone.
    /// </summary>
    DockStorehouse,

    /// <summary>
    /// True if player is on Costa Del Sol side of Eastern La Noscea, false if on Wineport side
    /// </summary>
    CostaDelSol,
    NewGamePlus,
    NotNewGamePlus,
}
