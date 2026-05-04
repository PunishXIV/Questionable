using FFXIVClientStructs.FFXIV.Component.GUI;
namespace Questionable.Utils;

internal static unsafe class AddonUtils
{
    public static AtkUnitBase* GetAddonById(uint addonId)
    {
        if (addonId == 0)
            return null;

        AtkStage* atkStage = AtkStage.Instance();
        if (atkStage == null || atkStage->RaptureAtkUnitManager == null)
            return null;

        return atkStage->RaptureAtkUnitManager->GetAddonById((ushort)addonId);
    }

    public static bool IsAddonReady(AtkUnitBase* addon)
    {
        return addon != null && addon->AtkValues != null;
    }
}
