using ECommons.ExcelServices;
namespace Questionable.Data;

internal static class GameDataAdapter
{
    public static bool DealsPhysicalDamage(Job classJob) => classJob.DealsPhysicalDamage();
    public static bool DealsMagicDamage(Job classJob) => classJob.DealsMagicDamage();
    public static bool IsCrafter(Job classJob) => classJob.IsCrafter();
    public static bool IsGatherer(Job classJob) => classJob.IsGatherer();
    public static bool IsCaster(Job classJob) => classJob.IsCaster();
    public static bool IsPhysicalRanged(Job classJob) => classJob.IsPhysicalRanged();
    public static bool IsMelee(Job classJob) => classJob.IsMelee();
    public static bool IsTank(Job classJob) => classJob.IsTank();
    public static bool IsHealer(Job classJob) => classJob.IsHealer();
    public static bool IsClass(Job classJob) => classJob.IsClass();
    public static Job AsJob(Job classJob) => classJob.AsJob();
}
