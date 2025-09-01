using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LLib.GameUI;
using Microsoft.Extensions.Logging;
using System;

namespace Questionable.Controller.GameUi;

internal sealed class HelpUiController : IDisposable
{
    private readonly QuestController _questController;
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IGameGui _gameGui;
    private readonly IFramework _framework;
    private readonly ILogger<HelpUiController> _logger;

    public HelpUiController(
        QuestController questController,
        IAddonLifecycle addonLifecycle,
        IGameGui gameGui,
        IFramework framework,
        ILogger<HelpUiController> logger)
    {
        _questController = questController;
        _addonLifecycle = addonLifecycle;
        _gameGui = gameGui;
        _framework = framework;
        _logger = logger;

        _questController.AutomationTypeChanged += CloseHelpWindowsWhenStartingQuests;

        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "AkatsukiNote", UnendingCodexPostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "ContentsTutorial", ContentsTutorialPostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "MultipleHelpWindow", MultipleHelpWindowPostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "JobHudNotice", JobHudNoticePostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "Guide", GuidePostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "EventTutorial", EventTutorialPostSetup);
    }

    private unsafe void CloseHelpWindowsWhenStartingQuests(object sender, QuestController.EAutomationType e)
    {
        if (e is QuestController.EAutomationType.Manual)
            return;

        if (_gameGui.TryGetAddonByName("Guide", out AtkUnitBase* addonGuide))
        {
            _logger.LogInformation("Guide window is open");
            GuidePostSetup(addonGuide);
        }

        if (_gameGui.TryGetAddonByName("EventTutorial", out AtkUnitBase* addonEventTutorial))
        {
            _logger.LogInformation("EventTutorial window is open");
            EventTutorialPostSetup(addonEventTutorial);
        }

        if (_gameGui.TryGetAddonByName("ContentsTutorial", out AtkUnitBase* addonContentsTutorial))
        {
            _logger.LogInformation("ContentsTutorial window is open");
            ContentsTutorialPostSetup(addonContentsTutorial);
        }

        if (_gameGui.TryGetAddonByName("JobHudNotice", out AtkUnitBase* addonJobHudNotice))
        {
            _logger.LogInformation("JobHudNotice window is open");
            JobHudNoticePostSetup(addonJobHudNotice);
        }
    }

    private unsafe void UnendingCodexPostSetup(AddonEvent type, AddonArgs args)
    {
        if (_questController.StartedQuest?.Quest.Id.Value == 4526)
        {
            _logger.LogInformation("Closing Unending Codex");
            AtkUnitBase* addon = (AtkUnitBase*)args.Addon.Address;
            addon->FireCallbackInt(-2);
        }
    }

    private unsafe void ContentsTutorialPostSetup(AddonEvent type, AddonArgs args)
    {
        if (_questController.StartedQuest?.Quest.Id.Value is 245 or 3872 or 5253)
            ContentsTutorialPostSetup((AtkUnitBase*)args.Addon.Address);
    }

    private unsafe void ContentsTutorialPostSetup(AtkUnitBase* addon)
    {
        _logger.LogInformation("Closing ContentsTutorial");
        addon->FireCallbackInt(13);
    }

    /// <summary>
    /// Opened e.g. the first time you open the duty finder window during Sastasha.
    /// </summary>
    private unsafe void MultipleHelpWindowPostSetup(AddonEvent type, AddonArgs args)
    {
        if (_questController.StartedQuest?.Quest.Id.Value == 245)
        {
            _logger.LogInformation("Closing MultipleHelpWindow");
            AtkUnitBase* addon = (AtkUnitBase*)args.Addon.Address;
            addon->FireCallbackInt(-2);
            addon->FireCallbackInt(-1);
        }
    }

    private unsafe void JobHudNoticePostSetup(AddonEvent type, AddonArgs args)
    {
        if (_questController.IsRunning || _questController.AutomationType != QuestController.EAutomationType.Manual)
            JobHudNoticePostSetup((AtkUnitBase*)args.Addon.Address);
    }

    private unsafe void JobHudNoticePostSetup(AtkUnitBase* addon)
    {
        _logger.LogInformation("Clicking the JobHudNotice window to open the relevant Guide page");
        addon->FireCallbackInt(0);
    }

    private unsafe void GuidePostSetup(AddonEvent type, AddonArgs args)
    {
        if (_questController.IsRunning || _questController.AutomationType != QuestController.EAutomationType.Manual)
            GuidePostSetup((AtkUnitBase*)args.Addon.Address);
    }

    private unsafe void GuidePostSetup(AtkUnitBase* addon)
    {
        _logger.LogInformation("Closing Guide window");
        addon->FireCallbackInt(-1);
    }

    private unsafe void EventTutorialPostSetup(AddonEvent type, AddonArgs args)
    {
        if (_questController.IsRunning || _questController.AutomationType != QuestController.EAutomationType.Manual)
        {
            // TODO Verify that this actually works; in initial testing it didn't close the window.
            _framework.RunOnTick(() =>
            {
                if (_gameGui.TryGetAddonByName("EventTutorial", out AtkUnitBase* addonEventTutorial))
                    EventTutorialPostSetup(addonEventTutorial);
            });
        }
    }

    private unsafe void EventTutorialPostSetup(AtkUnitBase* addon)
    {
        _logger.LogInformation("Closing EventTutorial window");
        addon->FireCallbackInt(-1);
    }


    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "EventTutorial", EventTutorialPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "Guide", GuidePostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "JobHudNotice", JobHudNoticePostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "MultipleHelpWindow", MultipleHelpWindowPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "ContentsTutorial", ContentsTutorialPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "AkatsukiNote", UnendingCodexPostSetup);

        _questController.AutomationTypeChanged -= CloseHelpWindowsWhenStartingQuests;
    }
}
