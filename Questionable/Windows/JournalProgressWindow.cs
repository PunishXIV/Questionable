using System;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Questionable.Controller;
using Questionable.Windows.Common;
using Questionable.Windows.JournalComponents;
namespace Questionable.Windows;

internal sealed class JournalProgressWindow : LWindow, IDisposable
{
    private readonly AlliedSocietyJournalComponent _alliedSocietyJournalComponent;
    private readonly IClientState _clientState;
    private readonly GatheringJournalComponent _gatheringJournalComponent;
    private readonly QuestJournalComponent _questJournalComponent;
    private readonly QuestRegistry _questRegistry;
    private readonly QuestRewardComponent _questRewardComponent;
    private readonly RedoComponent _redoComponent;

    public JournalProgressWindow(
        QuestJournalComponent questJournalComponent,
        QuestRewardComponent questRewardComponent,
        AlliedSocietyJournalComponent alliedSocietyJournalComponent,
        GatheringJournalComponent gatheringJournalComponent,
        RedoComponent redoComponent,
        QuestRegistry questRegistry,
        IClientState clientState)
        : base("Journal Progress###QuestionableJournalProgress")
    {
        _questJournalComponent = questJournalComponent;
        _alliedSocietyJournalComponent = alliedSocietyJournalComponent;
        _questRewardComponent = questRewardComponent;
        _gatheringJournalComponent = gatheringJournalComponent;
        _redoComponent = redoComponent;
        _questRegistry = questRegistry;
        _clientState = clientState;

        _clientState.Login += _questJournalComponent.RefreshCounts;
        _clientState.Login += _gatheringJournalComponent.RefreshCounts;
        _clientState.Logout += _questJournalComponent.ClearCounts;
        _clientState.Logout += _gatheringJournalComponent.ClearCounts;
        _questRegistry.Reloaded += OnQuestsReloaded;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new(200, 100)
        };
    }

    public void Dispose()
    {
        _questRegistry.Reloaded -= OnQuestsReloaded;
        _clientState.Logout -= _gatheringJournalComponent.ClearCounts;
        _clientState.Logout -= _questJournalComponent.ClearCounts;
        _clientState.Login -= _gatheringJournalComponent.RefreshCounts;
        _clientState.Login -= _questJournalComponent.RefreshCounts;
    }

    private void OnQuestsReloaded(object? sender, EventArgs e)
    {
        _questJournalComponent.RefreshCounts();
        _gatheringJournalComponent.RefreshCounts();
    }

    public override void OnOpen()
    {
        _questJournalComponent.UpdateFilter();
        _questJournalComponent.RefreshCounts();
        _gatheringJournalComponent.UpdateFilter();
        _gatheringJournalComponent.RefreshCounts();
    }

    public override void DrawContent()
    {
        using ImRaii.TabBarDisposable tabBar = ImRaii.TabBar("Journal");
        if (!tabBar)
            return;

        _questJournalComponent.DrawQuests();
        _alliedSocietyJournalComponent.DrawAlliedSocietyQuests();
        _questRewardComponent.DrawItemRewards();
        _gatheringJournalComponent.DrawGatheringItems();
        _redoComponent.DrawRedoChapters();
    }
}
