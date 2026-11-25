using static Questionable.External.IPCUtils;
namespace Questionable.External;

[RegisterSingleton]
internal sealed class LifestreamIpc : Ipc, IDisposable
{
    private readonly ILogger<LifestreamIpc> _logger;
    private readonly EzIPCDisposalToken[] _disposalTokens;

    [EzIPC("AethernetTeleport")] private readonly Func<string, bool> _aethernetByName = null!;
    [EzIPC("AethernetTeleportById")] private readonly Func<uint, bool> _aethernetById = null!;
    [EzIPC("AethernetTeleportByPlaceNameId")] private readonly Func<uint, bool> _aethernetByPlaceNameId = null!;
    [EzIPC("AethernetTeleportToFirmament")] private readonly Func<bool> _aethernetToFirmament = null!;
    [EzIPC("IsBusy")] private readonly Func<bool> _isBusy = null!;
    [EzIPC("ChangeCharacter")] private readonly Func<string, string, ErrorCode> _changeCharacter = null!;
    [EzIPC("EnqueueInnShortcut")] private readonly Action<int?> _innShortcut = null!;
    [EzIPC("EnqueuePropertyShortcut")] private readonly Action<EPropertyType, EHouseEnterMode?> _propertyShortcut = null!;
    [EzIPC("HasApartment")] private readonly Func<bool?> _hasApartment = null!;
    [EzIPC("HasPrivateHouse")] private readonly Func<bool?> _hasPrivateHouse = null!;
    [EzIPC("HasFreeCompanyHouse")] private readonly Func<bool?> _hasFreeCompanyHouse = null!;

    // Lifestream's PropertyType and HouseEnterMode, only for the calls' signatures.
    internal enum EPropertyType
    {
        Auto,
        Home,
        FC,
        Apartment,
        Inn,
    }

    internal enum EHouseEnterMode
    {
        None,
        WalkToDoor,
        EnterHouse,
    }

    public LifestreamIpc(ILogger<LifestreamIpc> logger)
    {
        _logger = logger;
        _disposalTokens = EzIPC.Init(this, InternalName, SafeWrapper.None);
    }

    public override string InternalName => "Lifestream";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IpcInvoke.SafeFunc(() => GetVersion() != null && !_isBusy(), fallback: false);

    public bool IsBusy => IpcInvoke.SafeFunc(() => _isBusy(), fallback: false);

    public bool GoInside(EPropertyType type)
    {
        _logger.LogInformation("Asking Lifestream to go to {Type}", type);
        return IpcInvoke.SafeFunc(() =>
        {
            if (type == EPropertyType.Inn)
                _innShortcut(null);
            else
                _propertyShortcut(type, EHouseEnterMode.EnterHouse);
            return true;
        }, false, _logger, "Unable to ask Lifestream for the trip");
    }

    public bool? HasProperty(EPropertyType type) => IpcInvoke.SafeFunc<bool?>(() => type switch
    {
        EPropertyType.Apartment => _hasApartment(),
        EPropertyType.Home => _hasPrivateHouse(),
        EPropertyType.FC => _hasFreeCompanyHouse(),
        _ => true,
    }, fallback: null);

    // Logs out first when in game; AutoRetainer's own relog call only works at the title screen.
    public bool ChangeCharacter(string name, string world)
    {
        ErrorCode result = IpcInvoke.SafeFunc(() => _changeCharacter(name, world), ErrorCode.Plugin_is_busy, _logger,
            "Unable to ask Lifestream to change character");
        _logger.LogInformation("Asked Lifestream to change to {Name}@{World}: {Result}", name, world, result);
        return result == ErrorCode.Success;
    }

    public bool Teleport(string destination)
    {
        _logger.LogInformation("Teleporting to vague string '{Destination}'", destination);
        return _aethernetByName(destination);
    }

    public bool Teleport(EAetheryteLocation aetheryteLocation)
    {
        _logger.LogInformation("Teleporting to '{Name}'", aetheryteLocation);
        return aetheryteLocation switch
        {
            EAetheryteLocation.IshgardFirmament => _aethernetToFirmament(),
            EAetheryteLocation.FirmamentMendicantsCourt => _aethernetByPlaceNameId(3436),
            EAetheryteLocation.FirmamentMattock => _aethernetByPlaceNameId(3473),
            EAetheryteLocation.FirmamentNewNest => _aethernetByPlaceNameId(3475),
            EAetheryteLocation.FirmanentSaintRoellesDais => _aethernetByPlaceNameId(3474),
            EAetheryteLocation.FirmamentFeatherfall => _aethernetByPlaceNameId(3525),
            EAetheryteLocation.FirmamentHoarfrostHall => _aethernetByPlaceNameId(3528),
            EAetheryteLocation.FirmamentWesternRisensongQuarter => _aethernetByPlaceNameId(3646),
            EAetheryteLocation.FirmamentEasternRisensongQuarter => _aethernetByPlaceNameId(3645),
            EAetheryteLocation.None => throw new ArgumentOutOfRangeException(nameof(aetheryteLocation)),
            var _ => _aethernetById((uint)aetheryteLocation)
        };
    }

    public void Dispose() => IPCSubscriber.DisposeAll(_disposalTokens);
}
