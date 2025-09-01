using Questionable.Model.Common.Converter;
using System.Collections.Generic;

namespace Questionable.Model.Questing.Converter;

public sealed class EmoteConverter() : EnumConverter<EEmote>(Values)
{
    private static readonly Dictionary<EEmote, string> Values = new()
    {
        { EEmote.Angry, "angry" },
        { EEmote.Bow, "bow" },
        { EEmote.Cheer, "cheer" },
        { EEmote.Clap, "clap" },
        { EEmote.Comfort, "comfort" },
        { EEmote.Cry, "cry" },
        { EEmote.Dance, "dance" },
        { EEmote.Doubt, "doubt" },
        { EEmote.Doze, "doze" },
        { EEmote.Wave, "wave" },
        { EEmote.Joy, "joy" },
        { EEmote.Kneel, "kneel" },
        { EEmote.Laugh, "laugh" },
        { EEmote.Lookout, "lookout" },
        { EEmote.Me, "me" },
        { EEmote.Deny, "deny" },
        { EEmote.Poke, "poke" },
        { EEmote.Psych, "psych" },
        { EEmote.Salute, "salute" },
        { EEmote.Rally, "rally" },
        { EEmote.Soothe, "soothe" },
        { EEmote.Stretch, "stretch" },
        { EEmote.Welcome, "welcome" },
        { EEmote.ExamineSelf, "examineself" },
        { EEmote.Happy, "happy" },
        { EEmote.Disappointed, "disappointed" },
        { EEmote.Pray, "pray" },
        { EEmote.ImperialSalute, "imperialsalute" },
        { EEmote.Pet, "pet" },
        { EEmote.Slap, "slap" },
        { EEmote.SundropDance, "sundropdance"},
        { EEmote.BattleStance, "battlestance" },
        { EEmote.VictoryPose, "victorypose" },
        { EEmote.MogDance, "mogdance" },
        { EEmote.Flex, "flex" },
        { EEmote.Respect, "respect" },
        { EEmote.Box, "box" },
        { EEmote.Greeting, "greeting" },
        { EEmote.Uchiwasshoi, "uchiwasshoi" },
        { EEmote.Unbound, "unbound" },
        { EEmote.Congratulate, "congratulate" },
        { EEmote.Beckon, "beckon" },
        { EEmote.Chuckle, "chuckle" },
        { EEmote.Hug, "hug" },
        { EEmote.Think, "think" }
    };
}
