using System.Collections.Generic;
namespace Questionable.Model;

public enum EExpansionVersion : byte
{
    ARealmReborn = 0,
    Heavensward = 1,
    Stormblood = 2,
    Shadowbringers = 3,
    Endwalker = 4,
    Dawntrail = 5,
    Evercold = 6
}

public static class ExpansionData
{
    public static IReadOnlyDictionary<EExpansionVersion, string> ExpansionFolders =
        new Dictionary<EExpansionVersion, string>
        {
            { EExpansionVersion.ARealmReborn, "2.x - A Realm Reborn" },
            { EExpansionVersion.Heavensward, "3.x - Heavensward" },
            { EExpansionVersion.Stormblood, "4.x - Stormblood" },
            { EExpansionVersion.Shadowbringers, "5.x - Shadowbringers" },
            { EExpansionVersion.Endwalker, "6.x - Endwalker" },
            { EExpansionVersion.Dawntrail, "7.x - Dawntrail" },
            { EExpansionVersion.Evercold, "8.x - Evercold" }
        };

    public static string ToFriendlyString(this EExpansionVersion expansionVersion) => expansionVersion switch
    {
        EExpansionVersion.ARealmReborn => "A Realm Reborn",
        var _ => expansionVersion.ToString()
    };
}
