using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using Questionable.Model.Common;
using Questionable.Utils;
namespace Questionable.External;

internal sealed class LifestreamIpc(IDalamudPluginInterface pluginInterface, ILogger<LifestreamIpc> logger)
{
#pragma warning disable CA1823 // Avoid unused private fields
    private readonly ICallGateSubscriber<string, bool> _aethernetTeleport =
        pluginInterface.GetIpcSubscriber<string, bool>("Lifestream.AethernetTeleport");
    private readonly ICallGateSubscriber<uint, bool> _aethernetTeleportById =
        pluginInterface.GetIpcSubscriber<uint, bool>("Lifestream.AethernetTeleportById");
    private readonly ICallGateSubscriber<uint, bool> _aethernetTeleportByPlaceNameId =
        pluginInterface.GetIpcSubscriber<uint, bool>("Lifestream.AethernetTeleportByPlaceNameId");
    private readonly ICallGateSubscriber<bool> _aethernetTeleportToFirmament =
        pluginInterface.GetIpcSubscriber<bool>("Lifestream.AethernetTeleportToFirmament");
    private readonly ICallGateSubscriber<bool> _isBusy =
        pluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
#pragma warning restore CA1823 // Avoid unused private fields

    public bool IsBusy => IpcInvoke.SafeFunc(() => _isBusy.InvokeFunc(), false);

    public bool Teleport(string destination)
    {
        logger.LogInformation("Teleporting to vague string '{Destination}'", destination);
        return _aethernetTeleport.InvokeFunc(destination);
    }

    public bool Teleport(EAetheryteLocation aetheryteLocation)
    {
        logger.LogInformation("Teleporting to '{Name}'", aetheryteLocation);
        return aetheryteLocation switch
        {
            EAetheryteLocation.IshgardFirmament => _aethernetTeleportToFirmament.InvokeFunc(),
            EAetheryteLocation.FirmamentMendicantsCourt => _aethernetTeleportByPlaceNameId.InvokeFunc(3436),
            EAetheryteLocation.FirmamentMattock => _aethernetTeleportByPlaceNameId.InvokeFunc(3473),
            EAetheryteLocation.FirmamentNewNest => _aethernetTeleportByPlaceNameId.InvokeFunc(3475),
            EAetheryteLocation.FirmanentSaintRoellesDais => _aethernetTeleportByPlaceNameId.InvokeFunc(3474),
            EAetheryteLocation.FirmamentFeatherfall => _aethernetTeleportByPlaceNameId.InvokeFunc(3525),
            EAetheryteLocation.FirmamentHoarfrostHall => _aethernetTeleportByPlaceNameId.InvokeFunc(3528),
            EAetheryteLocation.FirmamentWesternRisensongQuarter => _aethernetTeleportByPlaceNameId.InvokeFunc(3646),
            EAetheryteLocation.FirmamentEasternRisensongQuarter => _aethernetTeleportByPlaceNameId.InvokeFunc(3645),
            EAetheryteLocation.None => throw new ArgumentOutOfRangeException(nameof(aetheryteLocation)),
            var _ => _aethernetTeleportById.InvokeFunc((uint)aetheryteLocation)
        };
    }
}
