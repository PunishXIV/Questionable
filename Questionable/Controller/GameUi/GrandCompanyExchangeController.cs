using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Microsoft.Extensions.Logging;
using Questionable.Model.Questing;
using Questionable.Utils;

namespace Questionable.Controller.GameUi;

/// <summary>
///     Automates buying a Grand Company Chocobo Issuance from the GC quartermaster's
///     "Grand Company Exchange" window, so the "My Little Chocobo" quests (700/701/702) no
///     longer need manual intervention.
/// </summary>
/// <remarks>
///     Mirrors <see cref="ShopController" />, but the GC Exchange is a different addon/agent: it
///     is driven through <c>AgentGrandCompanyExchange.ReceiveEvent</c> rather than
///     <c>AddonMaster.Shop</c>. The controller acts only while a <see cref="EInteractionType.PurchaseItem" />
///     step is current and the <c>GrandCompanyExchange</c> window is open. The purchase
///     confirmation prompt is accepted by <see cref="YesNoChoiceHandler" /> via
///     <see cref="IsAwaitingYesNo" />.
/// </remarks>
internal sealed unsafe class GrandCompanyExchangeController : IDisposable
{
    private const string AddonName = "GrandCompanyExchange";
    private const int MaxPurchaseAttempts = 5;
    private static readonly TimeSpan YesNoTimeout = TimeSpan.FromSeconds(10);
    private const int MaterielTabValue0 = 2;
    private const int MaterielTabValue1 = 1;

    //   BuyChocoboLicense buys the chocobo issuance.
    //   BuyChocoboIssuanceSlot is its row index within the Materiel tab.
    private const int BuyValue0 = 0;
    private const int BuyChocoboIssuanceSlot = 6;
    private const int BuyQuantity = 1;

    private readonly QuestController _questController;
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IGameGuiAdapter _gameGuiAdapter;
    private readonly IFramework _framework;
    private readonly ILogger<GrandCompanyExchangeController> _logger;

    private EState _state = EState.Idle;
    private DateTime _continueAt = DateTime.MinValue;
    private DateTime _yesNoDeadline = DateTime.MinValue;
    private int _attempts;

    public GrandCompanyExchangeController(
        QuestController questController,
        IAddonLifecycle addonLifecycle,
        IGameGuiAdapter gameGuiAdapter,
        IFramework framework,
        ILogger<GrandCompanyExchangeController> logger)
    {
        _questController = questController;
        _addonLifecycle = addonLifecycle;
        _gameGuiAdapter = gameGuiAdapter;
        _framework = framework;
        _logger = logger;

        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, AddonName, OnPostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonName, OnPreFinalize);
        _framework.Update += OnFrameworkUpdate;
    }

    /// <summary>
    ///     Set while a purchase confirmation prompt is expected; <see cref="YesNoChoiceHandler" />
    ///     accepts the prompt and clears this flag. Mirrors <see cref="ShopController.IsAwaitingYesNo" />.
    /// </summary>
    public bool IsAwaitingYesNo { get; set; }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, AddonName, OnPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, AddonName, OnPreFinalize);
    }

    private bool IsOpen { get; set; }

    private void OnPostSetup(AddonEvent type, AddonArgs args)
    {
        IsOpen = true;
        _state = EState.Idle;
        _continueAt = DateTime.MinValue;
        _attempts = 0;
        IsAwaitingYesNo = false;
    }

    private void OnPreFinalize(AddonEvent type, AddonArgs args)
    {
        IsOpen = false;
        _state = EState.Idle;
        IsAwaitingYesNo = false;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!IsOpen || _state is EState.Done or EState.Aborted || !_questController.IsRunning)
            return;

        QuestStep? step = FindCurrentStep();
        if (step is not { InteractionType: EInteractionType.PurchaseItem, ItemId: { } itemId })
            return;

        int desired = Math.Max(1, step.ItemCount.GetValueOrDefault(1));
        if (GetItemCount(itemId) >= desired)
        {
            if (_state != EState.Done)
            {
                _logger.LogInformation("Chocobo issuance {ItemId} acquired; closing the exchange", itemId);
                IsAwaitingYesNo = false;
                _state = EState.Done;
                CloseWindow();
            }

            return;
        }

        // Wait for YesNoChoiceHandler to accept the purchase confirmation prompt.
        if (_state == EState.AwaitingYesNo)
        {
            if (!IsAwaitingYesNo)
            {
                _state = EState.Idle;
                _continueAt = DateTime.Now.AddSeconds(0.5);
            }
            else if (DateTime.Now >= _yesNoDeadline)
            {
                _logger.LogWarning("Timed out waiting for the purchase confirmation; retrying");
                IsAwaitingYesNo = false;
                _state = EState.Idle;
            }

            return;
        }

        if (DateTime.Now < _continueAt)
            return;

        AgentInterface* agent = GetAgent();
        if (agent == null || !agent->IsAgentActive())
            return;

        switch (_state)
        {
            case EState.Idle:
                if (_attempts >= MaxPurchaseAttempts)
                {
                    _logger.LogError(
                        "Gave up buying chocobo issuance {ItemId} after {Attempts} attempts; please buy it manually",
                        itemId, _attempts);
                    _state = EState.Aborted;
                    return;
                }

                _logger.LogInformation("Switching Grand Company Exchange to the Materiel tab");
                ChangeToMateriel(agent);
                _state = EState.TabSelected;
                _continueAt = DateTime.Now.AddSeconds(0.5);
                break;

            case EState.TabSelected:
                ++_attempts;
                _logger.LogInformation("Buying chocobo issuance {ItemId} (attempt {Attempt})", itemId, _attempts);
                IsAwaitingYesNo = true;
                BuyChocoboLicense(agent);
                _state = EState.AwaitingYesNo;
                _yesNoDeadline = DateTime.Now + YesNoTimeout;
                break;
        }
    }

    private QuestStep? FindCurrentStep()
    {
        QuestController.QuestProgress? currentQuest = _questController.CurrentQuest;
        QuestSequence? currentSequence = currentQuest?.Quest.FindSequence(currentQuest.Sequence);
        return currentSequence?.FindStep(currentQuest?.Step ?? 0);
    }

    private static AgentInterface* GetAgent() =>
        AgentModule.Instance()->GetAgentByInternalId(AgentId.GrandCompanyExchange);

    private static int GetItemCount(uint itemId) =>
        InventoryManager.Instance()->GetInventoryItemCount(itemId, checkEquipped: false, checkArmory: true);

    /// <summary>Closes the Grand Company Exchange window so the quest can move to the next step.</summary>
    private void CloseWindow()
    {
        if (_gameGuiAdapter.TryGetAddonByName(AddonName, out AtkUnitBase* addon))
            addon->FireCallbackInt(-1);
    }

    private static AtkValue ChangeToMateriel(AgentInterface* agent)
    {
        AtkValue returnValue = new();
        AtkValue* value = stackalloc AtkValue[2];
        value[0].Type = AtkValueType.Int;
        value[0].Int = MaterielTabValue0;
        value[1].Type = AtkValueType.Int;
        value[1].Int = MaterielTabValue1;
        agent->ReceiveEvent(&returnValue, value, 2, 0);
        return returnValue;
    }

    private static AtkValue BuyChocoboLicense(AgentInterface* agent)
    {
        AtkValue returnValue = new();
        AtkValue* value = stackalloc AtkValue[3];
        value[0].Type = AtkValueType.Int;
        value[0].Int = BuyValue0;
        value[1].Type = AtkValueType.Int;
        value[1].Int = BuyChocoboIssuanceSlot;
        value[2].Type = AtkValueType.Int;
        value[2].Int = BuyQuantity;
        agent->ReceiveEvent(&returnValue, value, 3, 0);
        return returnValue;
    }

    private enum EState
    {
        Idle,
        TabSelected,
        AwaitingYesNo,
        Done,
        Aborted
    }
}
