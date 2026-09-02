//Taken and adapted from https://github.com/awgil/ffxiv_navmesh/blob/master/vnavmesh/Movement/OverrideCamera.cs.

using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
namespace Questionable.Functions;

[RegisterSingleton]
internal sealed unsafe class CameraFunctions : IDisposable
{
    private readonly IFramework _framework;
    private readonly ILogger<CameraFunctions> _logger;
    private readonly IObjectTable _objectTable;

    private readonly bool IgnoreUserInput = true; // if true - override even if user tries to change camera orientation, otherwise override only if user does nothing
    [Signature("48 8B C4 53 48 81 EC ?? ?? ?? ?? 44 0F 29 50 ??")]
    private readonly Hook<RMICameraDelegate> _rmiCameraHook = null!;
    private float DesiredAltitude;
    private float DesiredAzimuth;

    public CameraFunctions(IGameInteropProvider gameInteropProvider, ILogger<CameraFunctions> logger, IObjectTable objectTable, IFramework framework)
    {
        _framework = framework;
        _logger = logger;
        gameInteropProvider.InitializeFromAttributes(this);
        _objectTable = objectTable;
    }

    public bool Enabled
    {
        get => _rmiCameraHook.IsEnabled;
        set
        {
            if (value)
                _rmiCameraHook.Enable();
            else
                _rmiCameraHook.Disable();
        }
    }

    public void Dispose() => IpcInvoke.TryOnFrameworkThread(_framework, () => _rmiCameraHook.Dispose(), _logger);

    private static float Deg2Rad(int degrees) => degrees * ((float)Math.PI / 180f);

    // from https://github.com/NightmareXIV/ECommons/blob/master/ECommons/MathHelpers/Angle.cs
    private static float Normalized(float r)
    {
        while (r < -MathF.PI)
        {
            r += 2 * MathF.PI;
        }

        while (r > MathF.PI)
        {
            r -= 2 * MathF.PI;
        }

        return r;
    }


    internal void Face(Vector3 pos)
    {
        _logger.LogDebug("Facing " + pos);
        Enabled = true;
        if (_objectTable[0] == null)
            return;
        Vector3 diff = pos - _objectTable[0]!.Position;
        DesiredAzimuth = MathF.Atan2(diff.X, diff.Z) + Deg2Rad(180);
        DesiredAltitude = Deg2Rad(-30);
    }

    private void RMICameraDetour(Camera* self, int inputMode, float speedH, float speedV)
    {
        _rmiCameraHook.Original(self, inputMode, speedH, speedV);
        if (IgnoreUserInput || inputMode == 0) // let user override...
        {
            float dt = Framework.Instance()->FrameDeltaTime;
            float deltaH = Normalized(DesiredAzimuth - self->DirH);
            float deltaV = Normalized(DesiredAltitude - self->DirV);
            float maxH = Deg2Rad(180);
            float maxV = Deg2Rad(180);
            self->InputDeltaH = Math.Clamp(deltaH, -maxH, maxH);
            self->InputDeltaV = Math.Clamp(deltaV, -maxV, maxV);
            Enabled = false;
        }
    }

    private delegate void RMICameraDelegate(Camera* self, int inputMode, float speedH, float speedV);
}
