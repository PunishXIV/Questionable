using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Questionable.Model;
using Questionable.Model.Common;
using Questionable.Model.Questing;
namespace Questionable.Data;

[SuppressMessage("Performance", "CA1822")]
internal sealed class AlliedSocietyData
{
    public ReadOnlyDictionary<ushort, AlliedSocietyMountConfiguration> Mounts { get; } =
        new Dictionary<ushort, AlliedSocietyMountConfiguration>
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

    public bool IsAlliedSocietyMount(ushort? mountId)
    {
        if (mountId is { } && Mounts.TryGetValue(mountId.Value, out AlliedSocietyMountConfiguration? mountConfig) && mountConfig is { })
            return true;
        return false;
    }
    public EAlliedSociety GetCommonAlliedSocietyTurnIn(ElementId elementId)
    {
        if (elementId is QuestId questId)
        {
            return questId.Value switch
            {
                >= 1222 and <= 1251 => EAlliedSociety.Amaljaa, //ARR
                //>= 1494 and <= 1523 => EAlliedSociety.Ixal, // Ixal also has 1566,1567,1568
                //>= 1566 and <= 1568 => EAlliedSociety.Ixal, // Ixal quests seem designed to be done sequentially and dont craft til 255
                >= 1325 and <= 1344 => EAlliedSociety.Kobolds, // Kobolds 1364-1373
                >= 1364 and <= 1373 => EAlliedSociety.Kobolds,
                >= 1257 and <= 1286 => EAlliedSociety.Sylphs,
                >= 2171 and <= 2200 => EAlliedSociety.VanuVanu, //HW
                >= 2261 and <= 2280 => EAlliedSociety.Vath,
                >= 2290 and <= 2319 => EAlliedSociety.Moogles,
                >= 3042 and <= 3069 => EAlliedSociety.Ananta, //Storm 
                >= 2979 and <= 3002 => EAlliedSociety.Kojin,
                >= 3103 and <= 3130 => EAlliedSociety.Namazu,
                >= 3902 and <= 3929 => EAlliedSociety.Dwarves, //SB
                >= 3689 and <= 3716 => EAlliedSociety.Pixies,
                >= 3806 and <= 3833 => EAlliedSociety.Qitari,
                >= 4551 and <= 4578 => EAlliedSociety.Arkasodara, //EW
                >= 4687 and <= 4714 => EAlliedSociety.Loporrits,
                >= 4607 and <= 4634 => EAlliedSociety.Omicrons,
                >= 5199 and <= 5226 => EAlliedSociety.Pelupelu, //Dawn
                >= 5261 and <= 5288 => EAlliedSociety.MamoolJa,
                >= 5336 and <= 5363 => EAlliedSociety.YokHuy,
                var _ => EAlliedSociety.None
            };
        }

        return EAlliedSociety.None;
    }

    public void GetCommonAlliedSocietyNpcs(EAlliedSociety alliedSociety, out uint[] normalNpcs, out uint[] mountNpcs)
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

public sealed record AlliedSocietyMountConfiguration(IReadOnlyList<uint> IssuerDataIds, EAetheryteLocation ClosestAetheryte);
