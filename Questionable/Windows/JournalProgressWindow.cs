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
    private readonly ContentProgressComponent _contentProgressComponent;
    private readonly TravelUnlockComponent _travelUnlockComponent;
    private readonly ZoneMapComponent _zoneMapComponent;
    private readonly QuestLinesComponent _questLinesComponent;

    public JournalProgressWindow(
        QuestJournalComponent questJournalComponent,
        QuestRewardComponent questRewardComponent,
        AlliedSocietyComponent alliedSocietyComponent,
        CustomDeliveryComponent customDeliveryComponent,
        RedoComponent redoComponent,
        QuestRegistry questRegistry,
        IClientState clientState,
        Configuration configuration,
        IDalamudPluginInterface pluginInterface,
        ContentProgressComponent contentProgressComponent,
        TravelUnlockComponent travelUnlockComponent,
        ZoneMapComponent zoneMapComponent,
        QuestLinesComponent questLinesComponent)
        : base(_L("Journal Progress") + "###QuestionableJournalProgress")
    {
        _configuration = configuration;
        _zoneMapComponent = zoneMapComponent;
        _questLinesComponent = questLinesComponent;
        _contentProgressComponent = contentProgressComponent;
        _travelUnlockComponent = travelUnlockComponent;
        _pluginInterface = pluginInterface;

        // Page ids are saved as the last open page; don't rename them.
        string completion = _L("Completion"), browse = _L("Browse"), societies = _L("Societies & deliveries");
        _pages =
        [
            new(completion, "Quests", _L("Quests"), FontAwesomeIcon.Tasks),
            new(completion, "Content", _L("Content"), FontAwesomeIcon.Dragon),
            new(completion, "TravelUnlocks", _L("Travel unlocks"), FontAwesomeIcon.MapMarkerAlt),
            new(completion, "Unlocks", _L("Unlocks"), FontAwesomeIcon.Star),
            new(completion, "NewGamePlus", _L("New Game+"), FontAwesomeIcon.History),
            new(browse, "ZoneMap", _L("Zone map"), FontAwesomeIcon.GlobeEurope),
            new(browse, "QuestLines", _L("Quest lines"), FontAwesomeIcon.ProjectDiagram),
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
            MinimumSize = new(880, 300)
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
            case "Content": _contentProgressComponent.Draw(); break;
            case "TravelUnlocks": _travelUnlockComponent.Draw(); break;
            case "ZoneMap": _zoneMapComponent.Draw(); break;
            case "QuestLines": _questLinesComponent.Draw(); break;
            case "Unlocks": _questRewardComponent.Draw(); break;
            case "NewGamePlus": _redoComponent.Draw(); break;
            case "AlliedSocieties": _alliedSocietyComponent.Draw(); break;
            case "CustomDeliveries": _customDeliveryComponent.Draw(); break;
        }
    }
}
