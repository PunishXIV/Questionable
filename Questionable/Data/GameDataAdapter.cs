using ECommons.ExcelServices;
namespace Questionable.Data;

internal static class GameDataAdapter
{
    public static bool DealsPhysicalDamage(Job classJob)
    {
        return classJob.DealsPhysicalDamage();
    }
    public static bool DealsMagicDamage(Job classJob)
    {
        return classJob.DealsMagicDamage();
    }
    public static bool IsCrafter(Job classJob)
    {
        return classJob.IsCrafter();
    }
    public static bool IsGatherer(Job classJob)
    {
        return classJob.IsGatherer();
    }
    public static bool IsCaster(Job classJob)
    {
        return classJob.IsCaster();
    }
    public static bool IsPhysicalRanged(Job classJob)
    {
        return classJob.IsPhysicalRanged();
    }
    public static bool IsMelee(Job classJob)
    {
        return classJob.IsMelee();
    }
    public static bool IsTank(Job classJob)
    {
        return classJob.IsTank();
    }
    public static bool IsHealer(Job classJob)
    {
        return classJob.IsHealer();
    }
    public static bool IsClass(Job classJob)
    {
        return classJob.IsClass();
    }
    public static Job AsJob(Job classJob)
    {
        return classJob.AsJob();
    }
}
