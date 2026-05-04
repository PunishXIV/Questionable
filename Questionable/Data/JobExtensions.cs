using System;
using System.Linq;
namespace ECommons.ExcelServices;

internal static class JobExtensions
{
    public static bool IsClass(this Job classJob)
    {
        return classJob is >= Job.GLA and <= Job.THM
                   or Job.ACN
                   or Job.ROG
               || classJob.IsCrafter()
               || classJob.IsGatherer();
    }

    public static bool HasBaseClass(this Job classJob)
    {
        return Enum.GetValues<Job>()
            .Where(x => x.IsClass())
            .Any(x => x.AsJob() == classJob);
    }

    public static Job AsJob(this Job classJob)
    {
        return classJob switch
        {
            Job.GLA => Job.PLD,
            Job.MRD => Job.WAR,
            Job.PGL => Job.MNK,
            Job.LNC => Job.DRG,
            Job.ROG => Job.NIN,
            Job.ARC => Job.BRD,
            Job.CNJ => Job.WHM,
            Job.THM => Job.BLM,
            Job.ACN => Job.SMN,
            _ => classJob
        };
    }

    public static bool IsMelee(this Job classJob)
    {
        return classJob is Job.PGL
            or Job.MNK
            or Job.LNC
            or Job.DRG
            or Job.ROG
            or Job.NIN
            or Job.SAM
            or Job.RPR
            or Job.VPR;
    }

    public static bool IsPhysicalRanged(this Job classJob)
    {
        return classJob is Job.ARC
            or Job.BRD
            or Job.MCH
            or Job.DNC;
    }

    public static bool IsCaster(this Job classJob)
    {
        return classJob is Job.THM
            or Job.BLM
            or Job.ACN
            or Job.SMN
            or Job.RDM
            or Job.BLU
            or Job.PCT;
    }

    public static bool DealsPhysicalDamage(this Job classJob)
    {
        return classJob.IsTank() || classJob.IsMelee() || classJob.IsPhysicalRanged();
    }

    public static bool DealsMagicDamage(this Job classJob)
    {
        return classJob.IsHealer() || classJob.IsCaster();
    }

    public static bool IsCrafter(this Job classJob)
    {
        return classJob.IsDoh();
    }

    public static bool IsGatherer(this Job classJob)
    {
        return classJob.IsDol();
    }

    public static string ToFriendlyString(this Job classJob)
    {
        return classJob switch
        {
            Job.WHM => "White Mage",
            Job.BLM => "Black Mage",
            Job.DRK => "Dark Knight",
            Job.RDM => "Red Mage",
            Job.BLU => "Blue Mage",
            var _ => classJob.ToString()
        };
    }
}
