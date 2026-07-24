using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Questionable.Model.Questing;

namespace Questionable.Controller.GameUi;

/// <summary>
///     Fills in the chocobo's name during the "My Little Chocobo" quests (700/701/702) using the
///     name configured in <see cref="Configuration.GeneralConfiguration.ChocoboName" />.
/// </summary>
/// <remarks>
///     Two steps: the <c>InputString</c> dialog is confirmed with a single callback
///     (<c>[0 = OK, name, ""]</c>), then the follow-up "Name your chocobo?" <c>SelectYesno</c> is
///     accepted by <see cref="YesNoChoiceHandler" /> via <see cref="IsAwaitingYesNo" />.
/// </remarks>
internal sealed unsafe class ChocoboNamingController : IDisposable
{
    private const string AddonName = "InputString";
    private const string DefaultName = "Chicken";
    private static readonly TimeSpan YesNoTimeout = TimeSpan.FromSeconds(10);

    private readonly QuestController _questController;
    private readonly Configuration _configuration;
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IGameGuiAdapter _gameGuiAdapter;
    private readonly IFramework _framework;

    private bool _namePending;
    private DateTime _continueAt = DateTime.MinValue;
    private DateTime _yesNoDeadline = DateTime.MinValue;

    public ChocoboNamingController(
        QuestController questController,
        Configuration configuration,
        IAddonLifecycle addonLifecycle,
        IGameGuiAdapter gameGuiAdapter,
        IFramework framework)
    {
        _questController = questController;
        _configuration = configuration;
        _addonLifecycle = addonLifecycle;
        _gameGuiAdapter = gameGuiAdapter;
        _framework = framework;

        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, AddonName, OnPostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonName, OnPreFinalize);
        _framework.Update += OnFrameworkUpdate;
    }

    /// <summary>
    ///     Set after the name is submitted; <see cref="YesNoChoiceHandler" /> accepts the follow-up
    ///     "Name your chocobo?" confirmation prompt and clears this.
    /// </summary>
    public bool IsAwaitingYesNo { get; set; }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, AddonName, OnPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, AddonName, OnPreFinalize);
    }

    private void OnPostSetup(AddonEvent type, AddonArgs args)
    {
        if (!_questController.IsRunning || !IsChocoboQuestActive())
            return;

        _namePending = true;
        _continueAt = DateTime.Now.AddSeconds(0.5);
    }

    private void OnPreFinalize(AddonEvent type, AddonArgs args) => _namePending = false;

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (_namePending && DateTime.Now >= _continueAt)
        {
            _namePending = false;

            // Confirm the InputString dialog: [0 = OK, name, ""]. A "Name your chocobo?"
            // SelectYesno follows, which YesNoChoiceHandler accepts via IsAwaitingYesNo.
            if (_gameGuiAdapter.TryGetAddonByName(AddonName, out AtkUnitBase* addon))
            {
                Callback.Fire(addon, updateState: true, 0, GetConfiguredName(), "");
                IsAwaitingYesNo = true;
                _yesNoDeadline = DateTime.Now + YesNoTimeout;
            }
        }
        else if (IsAwaitingYesNo && DateTime.Now >= _yesNoDeadline)
        {
            IsAwaitingYesNo = false;
        }
    }

    private bool IsChocoboQuestActive() =>
        _questController.CurrentQuest?.Quest.Id is QuestId { Value: 700 or 701 or 702 };

    /// <summary>The configured name, or <see cref="DefaultName" /> if it is unset or not a valid 2-20 letter name.</summary>
    private string GetConfiguredName()
    {
        string name = (_configuration.General.ChocoboName ?? string.Empty).Trim();
        return IsValidChocoboName(name) ? name : DefaultName;
    }

    private static bool IsValidChocoboName(string name)
    {
        if (name.Length is < 2 or > 20)
            return false;

        foreach (char c in name)
        {
            if (!char.IsLetter(c))
                return false;
        }

        return true;
    }
}
