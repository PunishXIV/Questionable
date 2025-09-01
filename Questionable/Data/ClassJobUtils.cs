using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Application.Network.WorkDefinitions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using LLib.GameData;
using Lumina.Excel.Sheets;
using Questionable.Model.Questing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Questionable.Data;

internal sealed class ClassJobUtils
{
    private readonly Configuration _configuration;
    private readonly ReadOnlyDictionary<EClassJob, sbyte> _classJobToExpArrayIndex;

    public ClassJobUtils(
        Configuration configuration,
        IDataManager dataManager)
    {
        _configuration = configuration;

        _classJobToExpArrayIndex = dataManager.GetExcelSheet<ClassJob>()
            .Where(x => x is { RowId: > 0, ExpArrayIndex: >= 0 })
            .ToDictionary(x => (EClassJob)x.RowId, x => x.ExpArrayIndex)
            .AsReadOnly();

        SortedClassJobs = dataManager.GetExcelSheet<ClassJob>()
            .Select(x => (ClassJob: (EClassJob)x.RowId, Priority: x.UIPriority))
            .OrderBy(x => x.Priority)
            .Select(x => (x.ClassJob, x.Priority / 10))
            .ToList()
            .AsReadOnly();
    }

    public readonly ReadOnlyCollection<(EClassJob ClassJob, int Category)> SortedClassJobs;

    public IEnumerable<EClassJob> AsIndividualJobs(EExtendedClassJob classJob, ElementId? referenceQuest)
    {
        return classJob switch
        {
            EExtendedClassJob.Gladiator => [EClassJob.Gladiator],
            EExtendedClassJob.Pugilist => [EClassJob.Pugilist],
            EExtendedClassJob.Marauder => [EClassJob.Marauder],
            EExtendedClassJob.Lancer => [EClassJob.Lancer],
            EExtendedClassJob.Archer => [EClassJob.Archer],
            EExtendedClassJob.Conjurer => [EClassJob.Conjurer],
            EExtendedClassJob.Thaumaturge => [EClassJob.Thaumaturge],
            EExtendedClassJob.Carpenter => [EClassJob.Carpenter],
            EExtendedClassJob.Blacksmith => [EClassJob.Blacksmith],
            EExtendedClassJob.Armorer => [EClassJob.Armorer],
            EExtendedClassJob.Goldsmith => [EClassJob.Goldsmith],
            EExtendedClassJob.Leatherworker => [EClassJob.Leatherworker],
            EExtendedClassJob.Weaver => [EClassJob.Weaver],
            EExtendedClassJob.Alchemist => [EClassJob.Alchemist],
            EExtendedClassJob.Culinarian => [EClassJob.Culinarian],
            EExtendedClassJob.Miner => [EClassJob.Miner],
            EExtendedClassJob.Botanist => [EClassJob.Botanist],
            EExtendedClassJob.Fisher => [EClassJob.Fisher],
            EExtendedClassJob.Paladin => [EClassJob.Paladin],
            EExtendedClassJob.Monk => [EClassJob.Monk],
            EExtendedClassJob.Warrior => [EClassJob.Warrior],
            EExtendedClassJob.Dragoon => [EClassJob.Dragoon],
            EExtendedClassJob.Bard => [EClassJob.Bard],
            EExtendedClassJob.WhiteMage => [EClassJob.WhiteMage],
            EExtendedClassJob.BlackMage => [EClassJob.BlackMage],
            EExtendedClassJob.Arcanist => [EClassJob.Arcanist],
            EExtendedClassJob.Summoner => [EClassJob.Summoner],
            EExtendedClassJob.Scholar => [EClassJob.Scholar],
            EExtendedClassJob.Rogue => [EClassJob.Rogue],
            EExtendedClassJob.Ninja => [EClassJob.Ninja],
            EExtendedClassJob.Machinist => [EClassJob.Machinist],
            EExtendedClassJob.DarkKnight => [EClassJob.DarkKnight],
            EExtendedClassJob.Astrologian => [EClassJob.Astrologian],
            EExtendedClassJob.Samurai => [EClassJob.Samurai],
            EExtendedClassJob.RedMage => [EClassJob.RedMage],
            EExtendedClassJob.BlueMage => [EClassJob.BlueMage],
            EExtendedClassJob.Gunbreaker => [EClassJob.Gunbreaker],
            EExtendedClassJob.Dancer => [EClassJob.Dancer],
            EExtendedClassJob.Reaper => [EClassJob.Reaper],
            EExtendedClassJob.Sage => [EClassJob.Sage],
            EExtendedClassJob.Viper => [EClassJob.Viper],
            EExtendedClassJob.Pictomancer => [EClassJob.Pictomancer],

            EExtendedClassJob.DoW => Enum.GetValues<EClassJob>().Where(x => x.DealsPhysicalDamage()),
            EExtendedClassJob.DoM => Enum.GetValues<EClassJob>().Where(x => x.DealsMagicDamage()),
            EExtendedClassJob.DoH => Enum.GetValues<EClassJob>().Where(x => x.IsCrafter()),
            EExtendedClassJob.DoL => Enum.GetValues<EClassJob>().Where(x => x.IsGatherer()),
            EExtendedClassJob.ConfiguredCombatJob => LookupConfiguredCombatJob() is var combatJob &&
                                                     combatJob != EClassJob.Adventurer
                ? [combatJob]
                : [],
            EExtendedClassJob.QuestStartJob => LookupQuestStartJob(referenceQuest) is var startJob &&
                                               startJob != EClassJob.Adventurer
                ? [startJob]
                : [],

            _ => throw new ArgumentOutOfRangeException(nameof(classJob), classJob, null)
        };
    }

    private EClassJob LookupConfiguredCombatJob()
    {
        var configuredJob = _configuration.General.CombatJob;
        var combatJobGearSets = GetCombatJobGearSets();
        HashSet<EClassJob> jobsWithGearSet = combatJobGearSets
            .Select(x => x.ClassJob)
            .Distinct()
            .ToHashSet();

        if (configuredJob != EClassJob.Adventurer)
        {
            if (jobsWithGearSet.Contains(configuredJob))
                return configuredJob;

            EClassJob baseClass = Enum.GetValues<EClassJob>()
                .SingleOrDefault(x => x.IsClass() && x.AsJob() == configuredJob);
            if (baseClass != EClassJob.Adventurer && jobsWithGearSet.Contains(baseClass))
                return baseClass;
        }

        return combatJobGearSets
            .OrderByDescending(x => x.Level)
            .ThenByDescending(x => x.ItemLevel)
            .ThenByDescending(x => x.ClassJob switch
            {
                _ when x.ClassJob.IsCaster() => 50,
                _ when x.ClassJob.IsPhysicalRanged() => 40,
                _ when x.ClassJob.IsMelee() => 30,
                _ when x.ClassJob.IsTank() => 20,
                _ when x.ClassJob.IsHealer() => 10,
                _ => 0,
            })
            .Select(x => x.ClassJob)
            .DefaultIfEmpty(EClassJob.Adventurer)
            .FirstOrDefault();
    }

    private unsafe ReadOnlyCollection<(EClassJob ClassJob, short Level, short ItemLevel)> GetCombatJobGearSets()
    {
        List<(EClassJob, short, short)> jobs = [];

        var playerState = PlayerState.Instance();
        var gearsetModule = RaptureGearsetModule.Instance();
        if (playerState == null || gearsetModule == null)
            return jobs.AsReadOnly();

        for (int i = 0; i < 100; ++i)
        {
            var gearset = gearsetModule->GetGearset(i);
            if (gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))
            {
                EClassJob classJob = (EClassJob)gearset->ClassJob;
                if (classJob.IsCrafter() || classJob.IsGatherer())
                    continue;

                short level = playerState->ClassJobLevels[_classJobToExpArrayIndex[classJob]];
                if (level == 0)
                    continue;

                short itemLevel = gearset->ItemLevel;
                jobs.Add((classJob, level, itemLevel));
            }
        }

        return jobs.AsReadOnly();
    }

    private unsafe EClassJob LookupQuestStartJob(ElementId? elementId)
    {
        ArgumentNullException.ThrowIfNull(elementId);

        if (elementId is QuestId questId)
        {
            QuestWork* questWork = QuestManager.Instance()->GetQuestById(questId.Value);
            if (questWork->AcceptClassJob != 0)
                return (EClassJob)questWork->AcceptClassJob;
        }

        return EClassJob.Adventurer;
    }
}
