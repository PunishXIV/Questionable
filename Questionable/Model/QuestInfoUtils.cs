using LLib.GameData;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Questionable.Model;

internal static class QuestInfoUtils
{
    private static readonly Dictionary<uint, IReadOnlyList<EClassJob>> CachedClassJobs = new();

    internal static IReadOnlyList<EClassJob> AsList(ClassJobCategory? optionalClassJobCategory)
    {
        if (optionalClassJobCategory == null)
            return Enum.GetValues<EClassJob>();

        ClassJobCategory classJobCategory = optionalClassJobCategory.Value;
        if (CachedClassJobs.TryGetValue(classJobCategory.RowId, out IReadOnlyList<EClassJob>? classJobs))
            return classJobs;

        classJobs = new Dictionary<EClassJob, bool>
            {
                { EClassJob.Adventurer, classJobCategory.ADV },
                { EClassJob.Gladiator, classJobCategory.GLA },
                { EClassJob.Pugilist, classJobCategory.PGL },
                { EClassJob.Marauder, classJobCategory.MRD },
                { EClassJob.Lancer, classJobCategory.LNC },
                { EClassJob.Archer, classJobCategory.ARC },
                { EClassJob.Conjurer, classJobCategory.CNJ },
                { EClassJob.Thaumaturge, classJobCategory.THM },
                { EClassJob.Carpenter, classJobCategory.CRP },
                { EClassJob.Blacksmith, classJobCategory.BSM },
                { EClassJob.Armorer, classJobCategory.ARM },
                { EClassJob.Goldsmith, classJobCategory.GSM },
                { EClassJob.Leatherworker, classJobCategory.LTW },
                { EClassJob.Weaver, classJobCategory.WVR },
                { EClassJob.Alchemist, classJobCategory.ALC },
                { EClassJob.Culinarian, classJobCategory.CUL },
                { EClassJob.Miner, classJobCategory.MIN },
                { EClassJob.Botanist, classJobCategory.BTN },
                { EClassJob.Fisher, classJobCategory.FSH },
                { EClassJob.Paladin, classJobCategory.PLD },
                { EClassJob.Monk, classJobCategory.MNK },
                { EClassJob.Warrior, classJobCategory.WAR },
                { EClassJob.Dragoon, classJobCategory.DRG },
                { EClassJob.Bard, classJobCategory.BRD },
                { EClassJob.WhiteMage, classJobCategory.WHM },
                { EClassJob.BlackMage, classJobCategory.BLM },
                { EClassJob.Arcanist, classJobCategory.ACN },
                { EClassJob.Summoner, classJobCategory.SMN },
                { EClassJob.Scholar, classJobCategory.SCH },
                { EClassJob.Rogue, classJobCategory.ROG },
                { EClassJob.Ninja, classJobCategory.NIN },
                { EClassJob.Machinist, classJobCategory.MCH },
                { EClassJob.DarkKnight, classJobCategory.DRK },
                { EClassJob.Astrologian, classJobCategory.AST },
                { EClassJob.Samurai, classJobCategory.SAM },
                { EClassJob.RedMage, classJobCategory.RDM },
                { EClassJob.BlueMage, classJobCategory.BLU },
                { EClassJob.Gunbreaker, classJobCategory.GNB },
                { EClassJob.Dancer, classJobCategory.DNC },
                { EClassJob.Reaper, classJobCategory.RPR },
                { EClassJob.Sage, classJobCategory.SGE },
                { EClassJob.Viper, classJobCategory.VPR },
                { EClassJob.Pictomancer, classJobCategory.PCT }
            }
            .Where(y => y.Value)
            .Select(y => y.Key)
            .ToList()
            .AsReadOnly();
        CachedClassJobs[classJobCategory.RowId] = classJobs;
        return classJobs;
    }
}
