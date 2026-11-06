using Questionable.Model.Common;
using static Questionable.External.IPCUtils;
namespace Questionable.External;

[RegisterSingleton]
internal sealed class LifestreamIpc : Ipc, IDisposable
{
    private readonly ILogger<LifestreamIpc> _logger;
    private readonly EzIPCDisposalToken[] _disposalTokens;

    [EzIPC("AethernetTeleport")] private readonly Func<string, bool> _aethernetTeleport = null!;
    [EzIPC("AethernetTeleportById")] private readonly Func<uint, bool> _aethernetTeleportById = null!;
    [EzIPC("AethernetTeleportByPlaceNameId")] private readonly Func<uint, bool> _aethernetTeleportByPlaceNameId = null!;
    [EzIPC("AethernetTeleportToFirmament")] private readonly Func<bool> _aethernetTeleportToFirmament = null!;
    [EzIPC("IsBusy")] private readonly Func<bool> _isBusy = null!;

    public LifestreamIpc(ILogger<LifestreamIpc> logger)
    {
        _logger = logger;
        _disposalTokens = EzIPC.Init(this, InternalName, SafeWrapper.None);
    }

    public override string InternalName => "Lifestream";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IpcInvoke.SafeFunc(() => GetVersion() != null && !_isBusy(), fallback: false);

    public bool IsBusy => IpcInvoke.SafeFunc(() => _isBusy(), fallback: false);

    public bool Teleport(string destination)
    {
        _logger.LogInformation("Teleporting to vague string '{Destination}'", destination);
        return _aethernetTeleport(destination);
    }

    public bool Teleport(EAetheryteLocation aetheryteLocation)
    {
        _logger.LogInformation("Teleporting to '{Name}'", aetheryteLocation);
        return aetheryteLocation switch
        {
            EAetheryteLocation.IshgardFirmament => _aethernetTeleportToFirmament(),
            EAetheryteLocation.FirmamentMendicantsCourt => _aethernetTeleportByPlaceNameId(3436),
            EAetheryteLocation.FirmamentMattock => _aethernetTeleportByPlaceNameId(3473),
            EAetheryteLocation.FirmamentNewNest => _aethernetTeleportByPlaceNameId(3475),
            EAetheryteLocation.FirmanentSaintRoellesDais => _aethernetTeleportByPlaceNameId(3474),
            EAetheryteLocation.FirmamentFeatherfall => _aethernetTeleportByPlaceNameId(3525),
            EAetheryteLocation.FirmamentHoarfrostHall => _aethernetTeleportByPlaceNameId(3528),
            EAetheryteLocation.FirmamentWesternRisensongQuarter => _aethernetTeleportByPlaceNameId(3646),
            EAetheryteLocation.FirmamentEasternRisensongQuarter => _aethernetTeleportByPlaceNameId(3645),
            EAetheryteLocation.None => throw new ArgumentOutOfRangeException(nameof(aetheryteLocation)),
            var _ => _aethernetTeleportById((uint)aetheryteLocation)
        };
    }

    public void Dispose() => IPCSubscriber.DisposeAll(_disposalTokens);
}
