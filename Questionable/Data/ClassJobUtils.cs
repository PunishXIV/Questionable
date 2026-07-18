using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Dalamud.Plugin.Services;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Application.Network.WorkDefinitions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using Questionable.Model.Questing;
namespace Questionable.Data;

internal sealed class ClassJobUtils
{
    private readonly ReadOnlyDictionary<Job, sbyte> _classJobToExpArrayIndex;
    private readonly Configuration _configuration;

    public readonly ReadOnlyCollection<(Job ClassJob, int Category)> SortedClassJobs;

    public ClassJobUtils(
        Configuration configuration,
        IDataManager dataManager)
    {
        _configuration = configuration;

        _classJobToExpArrayIndex = dataManager.GetExcelSheet<ClassJob>()
            .Where(x => x is { RowId: > 0, ExpArrayIndex: >= 0 })
            .ToDictionary(x => (Job)x.RowId, x => x.ExpArrayIndex)
            .AsReadOnly();

        SortedClassJobs = dataManager.GetExcelSheet<ClassJob>()
            .Select(x => (ClassJob: (Job)x.RowId, Priority: x.UIPriority))
            .OrderBy(x => x.Priority)
            .Select(x => (x.ClassJob, x.Priority / 10))
            .ToList()
            .AsReadOnly();
    }

    public IEnumerable<Job> AsIndividualJobs(EExtendedClassJob classJob, ElementId? referenceQuest)
    {
        return classJob switch
        {
            EExtendedClassJob.Gladiator => [Job.GLA],
            EExtendedClassJob.Pugilist => [Job.PGL],
            EExtendedClassJob.Marauder => [Job.MRD],
            EExtendedClassJob.Lancer => [Job.LNC],
            EExtendedClassJob.Archer => [Job.ARC],
            EExtendedClassJob.Conjurer => [Job.CNJ],
            EExtendedClassJob.Thaumaturge => [Job.THM],
            EExtendedClassJob.Carpenter => [Job.CRP],
            EExtendedClassJob.Blacksmith => [Job.BSM],
            EExtendedClassJob.Armorer => [Job.ARM],
            EExtendedClassJob.Goldsmith => [Job.GSM],
            EExtendedClassJob.Leatherworker => [Job.LTW],
            EExtendedClassJob.Weaver => [Job.WVR],
            EExtendedClassJob.Alchemist => [Job.ALC],
            EExtendedClassJob.Culinarian => [Job.CUL],
            EExtendedClassJob.Miner => [Job.MIN],
            EExtendedClassJob.Botanist => [Job.BTN],
            EExtendedClassJob.Fisher => [Job.FSH],
            EExtendedClassJob.Paladin => [Job.PLD],
            EExtendedClassJob.Monk => [Job.MNK],
            EExtendedClassJob.Warrior => [Job.WAR],
            EExtendedClassJob.Dragoon => [Job.DRG],
            EExtendedClassJob.Bard => [Job.BRD],
            EExtendedClassJob.WhiteMage => [Job.WHM],
            EExtendedClassJob.BlackMage => [Job.BLM],
            EExtendedClassJob.Arcanist => [Job.ACN],
            EExtendedClassJob.Summoner => [Job.SMN],
            EExtendedClassJob.Scholar => [Job.SCH],
            EExtendedClassJob.Rogue => [Job.ROG],
            EExtendedClassJob.Ninja => [Job.NIN],
            EExtendedClassJob.Machinist => [Job.MCH],
            EExtendedClassJob.DarkKnight => [Job.DRK],
            EExtendedClassJob.Astrologian => [Job.AST],
            EExtendedClassJob.Samurai => [Job.SAM],
            EExtendedClassJob.RedMage => [Job.RDM],
            EExtendedClassJob.BlueMage => [Job.BLU],
            EExtendedClassJob.Gunbreaker => [Job.GNB],
            EExtendedClassJob.Dancer => [Job.DNC],
            EExtendedClassJob.Reaper => [Job.RPR],
            EExtendedClassJob.Sage => [Job.SGE],
            EExtendedClassJob.Viper => [Job.VPR],
            EExtendedClassJob.Pictomancer => [Job.PCT],

            EExtendedClassJob.DoW => Enum.GetValues<Job>().Where(GameDataAdapter.DealsPhysicalDamage),
            EExtendedClassJob.DoM => Enum.GetValues<Job>().Where(GameDataAdapter.DealsMagicDamage),
            EExtendedClassJob.DoH => Enum.GetValues<Job>().Where(GameDataAdapter.IsCrafter),
            EExtendedClassJob.DoL => Enum.GetValues<Job>().Where(GameDataAdapter.IsGatherer),
            EExtendedClassJob.ConfiguredCombatJob => LookupConfiguredJob(EExtendedClassJob.ConfiguredCombatJob) is var combatJob &&
                                                     combatJob != Job.ADV
                ? [combatJob]
                : [],
            EExtendedClassJob.QuestStartJob => LookupQuestStartJob(referenceQuest) is var startJob &&
                                               startJob != Job.ADV
                ? [startJob]
                : [],
            EExtendedClassJob.ConfiguredCraftingJob => LookupConfiguredJob(EExtendedClassJob.DoH) is var craftJob &&
                                                       craftJob != Job.ADV
                ? [craftJob]
                : [],
            EExtendedClassJob.ConfiguredGatheringJob => LookupConfiguredJob(EExtendedClassJob.DoL) is var gatherJob &&
                                                        gatherJob != Job.ADV
                ? [gatherJob]
                : [],

            var _ => throw new ArgumentOutOfRangeException(nameof(classJob), classJob, message: null)
        };
    }

    private Job LookupConfiguredJob(EExtendedClassJob jobType)
    {
        Job configuredJob;
        if (jobType is EExtendedClassJob.ConfiguredCombatJob)
            configuredJob = _configuration.General.CombatJob;
        else if (jobType is EExtendedClassJob.DoH)
            configuredJob = _configuration.General.CraftingJob;
        else if (jobType is EExtendedClassJob.DoL)
            configuredJob = _configuration.General.GatheringJob;
        else
            return Job.ADV;
        ReadOnlyCollection<(Job ClassJob, short Level, short ItemLevel)> jobGearSets = GetJobGearSets(jobType is EExtendedClassJob.ConfiguredCombatJob);
        HashSet<Job> jobsWithGearSet = jobGearSets
            .Select(x => x.ClassJob)
            .Distinct()
            .ToHashSet();

        if (configuredJob != Job.ADV)
        {
            if (jobsWithGearSet.Contains(configuredJob))
                return configuredJob;

            Job baseClass = Enum.GetValues<Job>()
                .SingleOrDefault(x => GameDataAdapter.IsClass(x) && GameDataAdapter.AsJob(x) == configuredJob);
            if (baseClass != Job.ADV && jobsWithGearSet.Contains(baseClass))
                return baseClass;
        }

        return jobGearSets
            .OrderByDescending(x => x.Level)
            .ThenByDescending(x => x.ItemLevel)
            .ThenByDescending(x => x.ClassJob switch
            {
                var _ when GameDataAdapter.IsCaster(x.ClassJob) => 50,
                var _ when GameDataAdapter.IsPhysicalRanged(x.ClassJob) => 40,
                var _ when GameDataAdapter.IsMelee(x.ClassJob) => 30,
                var _ when GameDataAdapter.IsTank(x.ClassJob) => 20,
                var _ when GameDataAdapter.IsHealer(x.ClassJob) => 10,
                var _ => 0
            })
            .Select(x => x.ClassJob)
            .DefaultIfEmpty(Job.ADV)
            .FirstOrDefault();
    }

    private unsafe ReadOnlyCollection<(Job ClassJob, short Level, short ItemLevel)> GetJobGearSets(bool combat = true)
    {
        List<(Job, short, short)> jobs = [];

        PlayerState* playerState = PlayerState.Instance();
        RaptureGearsetModule* gearsetModule = RaptureGearsetModule.Instance();
        if (playerState == null || gearsetModule == null)
            return jobs.AsReadOnly();

        for (int i = 0; i < 100; ++i)
        {
            RaptureGearsetModule.GearsetEntry* gearset = gearsetModule->GetGearset(i);
            if (gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))
            {
                Job classJob = (Job)gearset->ClassJob;
                if (combat && (GameDataAdapter.IsCrafter(classJob) || GameDataAdapter.IsGatherer(classJob)))
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

    private unsafe Job LookupQuestStartJob(ElementId? elementId)
    {
        ArgumentNullException.ThrowIfNull(elementId);

        if (elementId is QuestId questId)
        {
            QuestWork* questWork = QuestManager.Instance()->GetQuestById(questId.Value);
            if (questWork->AcceptClassJob != 0)
                return (Job)questWork->AcceptClassJob;
        }

        return Job.ADV;
    }

    private readonly Dictionary<Job, (Job, ushort)> classToJobStone = new() {
                { Job.GLA, (Job.PLD, 4542) },
                { Job.PGL, (Job.MNK, 4543) },
                { Job.MRD, (Job.WAR, 4544) },
                { Job.LNC, (Job.DRG, 4545) },
                { Job.ARC, (Job.BRD, 4546) },
                { Job.CNJ, (Job.WHM, 4547) },
                { Job.THM, (Job.BLM, 4548) },
                { Job.ACN, (Job.SMN, 4549) },
                { Job.ROG, (Job.NIN, 7886) }
            };
    public (Job?, ushort?) ClassToJobStone(Job classJob)
    {
        if (classToJobStone.TryGetValue(classJob, out var value))
        {
            (var job, var item) = value;
            bool unlocked = false;
            unsafe
            {
                InventoryManager* inventoryManager = InventoryManager.Instance();
                if (inventoryManager->GetInventoryItemCount(item) > 0)
                    unlocked = true;
            }
            if (unlocked)
            {
                return (job, item);
            }
        }
        return (null, null);
    }

    public unsafe bool SwitchClassJob(Job classJob)
    {
        if (PlayerState.Instance()->CurrentClassJobId == (uint)classJob)
            return true;

        RaptureGearsetModule* gearsetModule = RaptureGearsetModule.Instance();
        if (gearsetModule != null)
        {
            for (int i = 0; i < 100; ++i)
            {
                RaptureGearsetModule.GearsetEntry* gearset = gearsetModule->GetGearset(i);
                if (gearset->ClassJob == (byte)classJob)
                {
                    gearsetModule->EquipGearset(gearset->Id);
                    return true;
                }
            }
        }
        return false;
    }
}
