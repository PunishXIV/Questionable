using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Questionable.Model.Questing;

namespace Questionable.Controller.GameUi;

/// <summary>
///     Automates buying a Grand Company Chocobo Issuance from the GC quartermaster's
///     "Grand Company Exchange" window, so the "My Little Chocobo" quests (700/701/702) no
///     longer need manual intervention.
/// </summary>
/// <remarks>
///     Mirrors <see cref="ShopController" />, but the GC Exchange is driven through
///     <c>AgentGrandCompanyExchange.ReceiveEvent</c>. The controller acts only while a
///     <see cref="EInteractionType.PurchaseItem" /> step is current and the
///     <c>GrandCompanyExchange</c> window is open. The purchase confirmation prompt is accepted by
///     <see cref="YesNoChoiceHandler" /> via <see cref="IsAwaitingYesNo" />.
/// </remarks>
internal sealed unsafe class GrandCompanyExchangeController : IDisposable
{
    private const string AddonName = "GrandCompanyExchange";
    private static readonly TimeSpan PurchaseTimeout = TimeSpan.FromSeconds(10);

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

    private EState _state = EState.Idle;
    private DateTime _continueAt = DateTime.MinValue;
    private DateTime _purchaseDeadline = DateTime.MinValue;

    public GrandCompanyExchangeController(
        QuestController questController,
        IAddonLifecycle addonLifecycle,
        IGameGuiAdapter gameGuiAdapter,
        IFramework framework)
    {
        _questController = questController;
        _addonLifecycle = addonLifecycle;
        _gameGuiAdapter = gameGuiAdapter;
        _framework = framework;

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
        if (!IsOpen || _state == EState.Done || !_questController.IsRunning)
            return;

        QuestStep? step = FindCurrentStep();
        if (step is not { InteractionType: EInteractionType.PurchaseItem, ItemId: { } itemId })
            return;

        int desired = Math.Max(1, step.ItemCount.GetValueOrDefault(1));
        if (GetItemCount(itemId) >= desired)
        {
            IsAwaitingYesNo = false;
            _state = EState.Done;
            CloseWindow();
            return;
        }

        // After the purchase is triggered, wait for the issuance to arrive (caught by the
        // inventory check above) or give up once the deadline passes.
        if (_state == EState.Purchasing)
        {
            if (DateTime.Now >= _purchaseDeadline)
            {
                IsAwaitingYesNo = false;
                _state = EState.Done;
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
                ChangeToMateriel(agent);
                _state = EState.TabSelected;
                _continueAt = DateTime.Now.AddSeconds(0.5);
                break;

            case EState.TabSelected:
                IsAwaitingYesNo = true;
                BuyChocoboLicense(agent);
                _state = EState.Purchasing;
                _purchaseDeadline = DateTime.Now + PurchaseTimeout;
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
        Purchasing,
        Done,
    }
}
