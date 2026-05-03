using LLib.GameData;
namespace Questionable.Data;

internal static class GameDataAdapter
{
    public static bool DealsPhysicalDamage(EClassJob classJob)
    {
        return classJob.DealsPhysicalDamage();
    }
    public static bool DealsMagicDamage(EClassJob classJob)
    {
        return classJob.DealsMagicDamage();
    }
    public static bool IsCrafter(EClassJob classJob)
    {
        return classJob.IsCrafter();
    }
    public static bool IsGatherer(EClassJob classJob)
    {
        return classJob.IsGatherer();
    }
    public static bool IsCaster(EClassJob classJob)
    {
        return classJob.IsCaster();
    }
    public static bool IsPhysicalRanged(EClassJob classJob)
    {
        return classJob.IsPhysicalRanged();
    }
    public static bool IsMelee(EClassJob classJob)
    {
        return classJob.IsMelee();
    }
    public static bool IsTank(EClassJob classJob)
    {
        return classJob.IsTank();
    }
    public static bool IsHealer(EClassJob classJob)
    {
        return classJob.IsHealer();
    }
    public static bool IsClass(EClassJob classJob)
    {
        return classJob.IsClass();
    }
    public static EClassJob AsJob(EClassJob classJob)
    {
        return classJob.AsJob();
    }
}
