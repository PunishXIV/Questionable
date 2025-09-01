using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Questionable.Controller;
using Questionable.Data;
using Questionable.Model.Questing;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace Questionable.Windows;

internal sealed class DebugOverlay : Window
{
    private readonly QuestController _questController;
    private readonly QuestRegistry _questRegistry;
    private readonly IGameGui _gameGui;
    private readonly IClientState _clientState;
    private readonly ICondition _condition;
    private readonly AetheryteData _aetheryteData;
    private readonly IObjectTable _objectTable;
    private readonly CombatController _combatController;
    private readonly Configuration _configuration;

    public DebugOverlay(QuestController questController, QuestRegistry questRegistry, IGameGui gameGui,
        IClientState clientState, ICondition condition, AetheryteData aetheryteData, IObjectTable objectTable,
        CombatController combatController, Configuration configuration)
        : base("Questionable Debug Overlay###QuestionableDebugOverlay",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground |
            ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoSavedSettings, true)
    {
        _questController = questController;
        _questRegistry = questRegistry;
        _gameGui = gameGui;
        _clientState = clientState;
        _condition = condition;
        _aetheryteData = aetheryteData;
        _objectTable = objectTable;
        _combatController = combatController;
        _configuration = configuration;

        Position = Vector2.Zero;
        PositionCondition = ImGuiCond.Always;
        Size = ImGui.GetIO().DisplaySize;
        SizeCondition = ImGuiCond.Always;
        IsOpen = true;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
    }

    public ElementId? HighlightedQuest { get; set; }

    public override bool DrawConditions() => _configuration.Advanced.DebugOverlay;

    public override void PreDraw()
    {
        Size = ImGui.GetIO().DisplaySize;
    }

    public override void Draw()
    {
        if (_condition[ConditionFlag.OccupiedInCutSceneEvent])
            return;

        if (_clientState is not { IsLoggedIn: true, LocalPlayer: not null, IsPvPExcludingDen: false })
            return;

        if (!_questController.IsQuestWindowOpen)
            return;

        DrawCurrentQuest();
        DrawHighlightedQuest();

        if (_configuration.Advanced.CombatDataOverlay)
            DrawCombatTargets();
    }

    private void DrawCurrentQuest()
    {
        var currentQuest = _questController.CurrentQuest;
        if (currentQuest == null)
            return;

        var sequence = currentQuest.Quest.FindSequence(currentQuest.Sequence);
        if (sequence == null)
            return;

        for (int i = currentQuest.Step; i <= sequence.Steps.Count; ++i)
        {
            QuestStep? step = sequence.FindStep(i);
            if (step != null && TryGetPosition(step, out Vector3? position))
            {
                DrawStep(i.ToString(CultureInfo.InvariantCulture), step, position.Value,
                    Vector3.Distance(_clientState.LocalPlayer!.Position, position.Value) <
                    step.CalculateActualStopDistance()
                        ? 0xFF00FF00
                        : 0xFF0000FF);
            }
        }
    }

    private void DrawHighlightedQuest()
    {
        if (HighlightedQuest == null || !_questRegistry.TryGetQuest(HighlightedQuest, out var quest))
            return;

        foreach (var sequence in quest.Root.QuestSequence)
        {
            for (int i = 0; i < sequence.Steps.Count; ++i)
            {
                QuestStep? step = sequence.FindStep(i);
                if (step != null && TryGetPosition(step, out Vector3? position))
                {
                    DrawStep($"{quest.Id} / {sequence.Sequence} / {i}", step, position.Value, 0xFFFFFFFF);
                }
            }
        }
    }

    private void DrawStep(string counter, QuestStep step, Vector3 position, uint color)
    {
        if (step.Disabled || step.TerritoryId != _clientState.TerritoryType)
            return;

        bool visible = _gameGui.WorldToScreen(position, out Vector2 screenPos);
        if (!visible)
            return;

        ImGui.GetWindowDrawList().AddCircleFilled(screenPos, 3f, color);
        ImGui.GetWindowDrawList().AddText(screenPos + new Vector2(10, -8), color,
            $"{counter}: {step.InteractionType}\n{position.ToString("G", CultureInfo.InvariantCulture)} [{(position - _clientState.LocalPlayer!.Position).Length():N2}]\n{step.Comment}");
    }

    private void DrawCombatTargets()
    {
        if (!_combatController.IsRunning)
            return;

        foreach (var x in _objectTable.Skip(1))
        {
            if (x is not IBattleNpc)
                continue;

            bool visible = _gameGui.WorldToScreen(x.Position, out Vector2 screenPos);
            if (!visible)
                continue;

            var (priority, reason) = _combatController.GetKillPriority(x);
            ImGui.GetWindowDrawList().AddText(screenPos + new Vector2(10, -8), priority > 0 ? 0xFF00FF00 : 0xFFFFFFFF,
                $"{x.Name}/{x.GameObjectId:X}, {x.DataId}, {priority} - {reason}, {Vector3.Distance(x.Position, _clientState.LocalPlayer!.Position):N2}, {x.IsTargetable}");
        }
    }

    private bool TryGetPosition(QuestStep step, [NotNullWhen(true)] out Vector3? position)
    {
        if (step.Position != null)
        {
            position = step.Position;
            return true;
        }
        else if (step is { InteractionType: EInteractionType.AttuneAetheryte or EInteractionType.RegisterFreeOrFavoredAetheryte, Aetheryte: { } aetheryteLocation })
        {
            position = _aetheryteData.Locations[aetheryteLocation];
            return true;
        }
        else if (step is { InteractionType: EInteractionType.AttuneAethernetShard, AethernetShard: { } aethernetShard })
        {
            position = _aetheryteData.Locations[aethernetShard];
            return true;
        }
        else
        {
            position = null;
            return false;
        }
    }
}
