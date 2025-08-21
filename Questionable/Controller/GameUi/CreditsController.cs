using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Microsoft.Extensions.Logging;

namespace Questionable.Controller.GameUi;

internal sealed class CreditsController : IDisposable
{
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly ILogger<CreditsController> _logger;

    public CreditsController(IAddonLifecycle addonLifecycle, ILogger<CreditsController> logger)
    {
        _addonLifecycle = addonLifecycle;
        _logger = logger;

        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "CreditScroll", CreditScrollPostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "Credit", CreditPostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "CreditPlayer", CreditPlayerPostSetup);
    }


    /// <summary>
    /// ARR Credits.
    /// </summary>
    private unsafe void CreditScrollPostSetup(AddonEvent type, AddonArgs args)
    {
        _logger.LogInformation("Closing Credits sequence");
        AtkUnitBase* addon = (AtkUnitBase*)args.Addon.Address;
        addon->FireCallbackInt(-2);
    }

    /// <summary>
    /// Credits for (possibly all?) expansions, not used for ARR.
    /// </summary>
    private unsafe void CreditPostSetup(AddonEvent type, AddonArgs args)
    {
        _logger.LogInformation("Closing Credits sequence");
        AtkUnitBase* addon = (AtkUnitBase*)args.Addon.Address;
        addon->FireCallbackInt(-2);
    }

    private unsafe void CreditPlayerPostSetup(AddonEvent type, AddonArgs args)
    {
        _logger.LogInformation("Closing CreditPlayer");
        AtkUnitBase* addon = (AtkUnitBase*)args.Addon.Address;
        addon->Close(true);
    }

    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "CreditPlayer", CreditPlayerPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "Credit", CreditPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "CreditScroll", CreditScrollPostSetup);
    }
}
