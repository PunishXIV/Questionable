using System.Collections.ObjectModel;
namespace Questionable.Data;

[SuppressMessage("Performance", "CA1822")]
[RegisterSingleton]
internal sealed class AlliedSocietyData
{
    public ReadOnlyDictionary<ushort, AlliedSocietyMount> Mounts { get; } =
        new Dictionary<ushort, AlliedSocietyMount>
        {
            { 23, new([1008327, 1005848, 1005860, 1005581], EAetheryteLocation.SouthernThanalanLittleAlaMhigo) }, // amaljaa
            { 24, new([1052562, 1008332], EAetheryteLocation.EastShroudHawthorneHut) }, // sylphs
            { 66, new([1016093, 1016087], EAetheryteLocation.SeaOfCloudsOkZundu) }, //vanu
            { 79, new([1017031, 1016837, 1016838], EAetheryteLocation.DravanianForelandsAnyxTrine) }, //vath
            { 88, new([1017470, 1017432], EAetheryteLocation.ChurningMistsZenith) }, //moogle
            { 89, new([1017322], EAetheryteLocation.ChurningMistsZenith) }, //moogle
            { 137, new([1024218], EAetheryteLocation.RubySeaTamamizu) }, //Kojin  
            { 147, new([1024777, 1024912], EAetheryteLocation.FringesPeeringStones) }, //ananta
            { 165, new([1025610], EAetheryteLocation.AzimSteppeDhoroIloh) }, //namazu
            { 210, new([1031811], EAetheryteLocation.IlMhegLydhaLran) }, //Pixie
            { 221, new([1032663], EAetheryteLocation.RaktikaFanow) }, //Qitari Done
            { 227, new([1033715], EAetheryteLocation.LakelandOstallImperative) }, //Dwarf
            { 296, new([1042302], EAetheryteLocation.ThavnairYedlihmad) }, //Arkadosa
            { 307, new([1044060, 1044072, 1044066], EAetheryteLocation.UltimaThuleBaseOmicron) }, //Omicron
            { 309, new([1044408, 1044403], EAetheryteLocation.MareLamentorumBestwaysBurrow) }, // rabbits
            { 369, new([1051798], EAetheryteLocation.KozamaukaDockPoga) }, //pelu
            { 391, new([1052562], EAetheryteLocation.YakTelMamook) }, //mamook
        }.AsReadOnly();
    // Sylph 1257 has always been left out; Ixal 1569 Deliverance isn't a normal daily.
    private static readonly ushort[] NotBatched = [1257, 1569];

    public EAlliedSociety GetCommonTurnIn(IQuestInfo? info) =>
        info is QuestInfo { IsRepeatable: true, AlliedSociety: not EAlliedSociety.None, QuestId: QuestId id } quest &&
        !NotBatched.Contains(id.Value)
            ? quest.AlliedSociety
            : EAlliedSociety.None;

    public void GetCommonNpcs(EAlliedSociety alliedSociety, out uint[] normalNpcs, out uint[] mountNpcs)
    {
        if (alliedSociety == EAlliedSociety.Ixal)
        {
            normalNpcs = [1008945, 1009218, 1009217, 1009219, 1009220, 1009221];
            mountNpcs = [];
        }
        else if (alliedSociety == EAlliedSociety.VanuVanu)
        {
            normalNpcs = [1016088, 1016091, 1016092];
            mountNpcs = [1016093];
        }
        else if (alliedSociety == EAlliedSociety.Vath)
        {
            normalNpcs = [];
            mountNpcs = [1017031];
        }
        else if (alliedSociety == EAlliedSociety.Moogles)
        {
            normalNpcs = [];
            mountNpcs = [1017322, 1017470, 1017471];
        }
        else
        {
            normalNpcs = [];
            mountNpcs = [];
        }
    }
}
