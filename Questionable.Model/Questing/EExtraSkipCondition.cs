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

    // ROG quest location far beneath the actual lower decks.
    RoguesGuild,
    NotRoguesGuild,

    // NIN quest location far beneath the actual zone.
    DockStorehouse,

    // True on the Costa Del Sol side of Eastern La Noscea; false on the Wineport side.
    CostaDelSol,
    NewGamePlus,
    NotNewGamePlus,
}
