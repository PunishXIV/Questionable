namespace Questionable.Windows;

[RegisterSingleton]
internal sealed class JournalProgressWindow : LWindow, IDisposable
{
    private readonly AlliedSocietyComponent _alliedSocietyComponent;
    private readonly CustomDeliveryComponent _customDeliveryComponent;
    private readonly IClientState _clientState;
    private readonly QuestJournalComponent _questJournalComponent;
    private readonly QuestRegistry _questRegistry;
    private readonly QuestRewardComponent _questRewardComponent;
    private readonly RedoComponent _redoComponent;
    private readonly Configuration _configuration;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly QstWidgets.MenuPage[] _pages;

    public JournalProgressWindow(
        QuestJournalComponent questJournalComponent,
        QuestRewardComponent questRewardComponent,
        AlliedSocietyComponent alliedSocietyComponent,
        CustomDeliveryComponent customDeliveryComponent,
        RedoComponent redoComponent,
        QuestRegistry questRegistry,
        IClientState clientState,
        Configuration configuration,
        IDalamudPluginInterface pluginInterface)
        : base(_L("Journal Progress") + "###QuestionableJournalProgress")
    {
        _configuration = configuration;
        _pluginInterface = pluginInterface;

        // Page ids are saved as the last open page; don't rename them.
        string completion = _L("Completion"), societies = _L("Societies & deliveries");
        _pages =
        [
            new(completion, "Quests", _L("Quests"), FontAwesomeIcon.Tasks),
            new(completion, "Unlocks", _L("Unlocks"), FontAwesomeIcon.Star),
            new(completion, "NewGamePlus", _L("New Game+"), FontAwesomeIcon.History),
            new(societies, "AlliedSocieties", _L("Allied Societies"), FontAwesomeIcon.Feather),
            new(societies, "CustomDeliveries", _L("Custom Deliveries"), FontAwesomeIcon.BoxOpen),
        ];

        _questJournalComponent = questJournalComponent;
        _alliedSocietyComponent = alliedSocietyComponent;
        _customDeliveryComponent = customDeliveryComponent;
        _questRewardComponent = questRewardComponent;
        _redoComponent = redoComponent;
        _questRegistry = questRegistry;
        _clientState = clientState;

        _clientState.Login += _questJournalComponent.RefreshCounts;
        _clientState.Logout += _questJournalComponent.ClearCounts;
        _questRegistry.Reloaded += OnQuestsReloaded;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new(200, 100)
        };
    }

    public void Dispose()
    {
        _questRegistry.Reloaded -= OnQuestsReloaded;
        _clientState.Logout -= _questJournalComponent.ClearCounts;
        _clientState.Login -= _questJournalComponent.RefreshCounts;
    }

    private void OnQuestsReloaded(object? sender, EventArgs e)
    {
        _questJournalComponent.RefreshCounts();
        _questRewardComponent.ClearCache();
    }

    public override void OnOpen()
    {
        _questJournalComponent.UpdateFilter();
        _questJournalComponent.RefreshCounts();
    }

    public override void DrawContent()
    {
        WindowConfig windowConfig = _configuration.JournalWindowConfig;
        string page = windowConfig.Page;
        using ImRaii.ChildDisposable content = QstWidgets.SideMenu("Journal", _pages, ref page);
        if (page != windowConfig.Page)
        {
            windowConfig.Page = page;
            _pluginInterface.SavePluginConfig(_configuration);
        }

        if (!content)
            return;

        switch (page)
        {
            case "Quests": _questJournalComponent.Draw(); break;
            case "Unlocks": _questRewardComponent.Draw(); break;
            case "NewGamePlus": _redoComponent.Draw(); break;
            case "AlliedSocieties": _alliedSocietyComponent.Draw(); break;
            case "CustomDeliveries": _customDeliveryComponent.Draw(); break;
        }
    }
}
