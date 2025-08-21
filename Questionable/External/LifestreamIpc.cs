using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Microsoft.Extensions.Logging;
using Questionable.Model.Common;

namespace Questionable.External;

internal sealed class LifestreamIpc
{
    private readonly ILogger<LifestreamIpc> _logger;
    private readonly ICallGateSubscriber<uint, bool> _aethernetTeleportByPlaceNameId;
    private readonly ICallGateSubscriber<uint, bool> _aethernetTeleportById;
    private readonly ICallGateSubscriber<bool> _aethernetTeleportToFirmament;

    public LifestreamIpc(IDalamudPluginInterface pluginInterface, ILogger<LifestreamIpc> logger)
    {
        _logger = logger;
        _aethernetTeleportByPlaceNameId =
            pluginInterface.GetIpcSubscriber<uint, bool>("Lifestream.AethernetTeleportByPlaceNameId");
        _aethernetTeleportById =
            pluginInterface.GetIpcSubscriber<uint, bool>("Lifestream.AethernetTeleportById");
        _aethernetTeleportToFirmament =
            pluginInterface.GetIpcSubscriber<bool>("Lifestream.AethernetTeleportToFirmament");
    }

    public bool Teleport(EAetheryteLocation aetheryteLocation)
    {
        _logger.LogInformation("Teleporting to '{Name}'", aetheryteLocation);
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
            EAetheryteLocation.FIrmamentEasternRisensongQuarter => _aethernetTeleportByPlaceNameId.InvokeFunc(3645),
            EAetheryteLocation.None => throw new ArgumentOutOfRangeException(nameof(aetheryteLocation)),
            _ => _aethernetTeleportById.InvokeFunc((uint)aetheryteLocation),
        };
    }
}
