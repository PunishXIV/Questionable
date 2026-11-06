using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace Questionable.Controller;

[RegisterSingleton]
internal sealed unsafe class ProfileController : IDisposable
{
    private readonly IClientState _clientState;
    private readonly IFramework _framework;
    private readonly Configuration _configuration;
    private readonly ILogger<ProfileController> _logger;

    public ProfileController(IClientState clientState, IFramework framework, Configuration configuration,
        ILogger<ProfileController> logger)
    {
        _clientState = clientState;
        _framework = framework;
        _configuration = configuration;
        _logger = logger;

        _clientState.Login += OnLogin;
        _clientState.Logout += OnLogout;
        if (_clientState.IsLoggedIn)
            _framework.RunOnFrameworkThread(ActivateForCurrentCharacter);
    }

    public static string? CurrentContentId
    {
        get
        {
            PlayerState* playerState = PlayerState.Instance();
            return playerState != null && playerState->ContentId != 0
                ? playerState->ContentId.ToString(CultureInfo.InvariantCulture)
                : null;
        }
    }

    public void ActivateForCurrentCharacter()
    {
        string? contentId = CurrentContentId;
        if (contentId == null)
            _configuration.ClearActiveProfile();
        else
            _configuration.ActivateForCharacter(contentId);

        _logger.LogInformation("Using settings profile: {Profile}", _configuration.ActiveProfileName ?? "default");
    }

    private void OnLogin() => _framework.RunOnFrameworkThread(ActivateForCurrentCharacter);

    private void OnLogout(int type, int code) => _configuration.ClearActiveProfile();

    public void Dispose()
    {
        _clientState.Logout -= OnLogout;
        _clientState.Login -= OnLogin;
    }
}
