using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
namespace Questionable.IpcTest;

// ReSharper disable once InconsistentNaming
public sealed class IpcTestPlugin : IDalamudPlugin
{
    private readonly IChatGui _chatGui;
    private readonly ICommandManager _commandManager;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ICallGateSubscriber<string, string, object> _stopped;
    private readonly ICallGateSubscriber<string, object> _questCompleted;
    private readonly ICallGateSubscriber<string, byte, int, object> _stepChanged;

    public IpcTestPlugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IChatGui chatGui)
    {
        _pluginInterface = pluginInterface;
        _commandManager = commandManager;
        _chatGui = chatGui;

        commandManager.AddHandler("/qipc", new(ProcessCommand));

        _stopped = pluginInterface.GetIpcSubscriber<string, string, object>("Questionable.Stopped");
        _stopped.Subscribe(OnStopped);
        _questCompleted = pluginInterface.GetIpcSubscriber<string, object>("Questionable.QuestCompleted");
        _questCompleted.Subscribe(OnQuestCompleted);
        _stepChanged = pluginInterface.GetIpcSubscriber<string, byte, int, object>("Questionable.StepChanged");
        _stepChanged.Subscribe(OnStepChanged);
    }

    public void Dispose()
    {
        _stopped.Unsubscribe(OnStopped);
        _questCompleted.Unsubscribe(OnQuestCompleted);
        _stepChanged.Unsubscribe(OnStepChanged);
        _commandManager.RemoveHandler("/qipc");
    }

    private void OnStopped(string kind, string reason) => Print($"Stopped: [{kind}] {reason}");

    private void OnQuestCompleted(string questId) => Print($"Quest completed: {questId}");

    private void OnStepChanged(string questId, byte sequence, int step) =>
        Print(string.Create(CultureInfo.InvariantCulture, $"Step: {questId} {sequence}/{step}"));

    private void Print(string text) =>
        _chatGui.Print(new SeStringBuilder().AddUiForeground("[IPC]", 576).AddText(": " + text).Build());

    private void ProcessCommand(string command, string arguments)
    {
        if (arguments == "stepdata")
        {
            IpcStepData? stepData = _pluginInterface.GetIpcSubscriber<IpcStepData?>("Questionable.GetCurrentStepData").InvokeFunc();
            _chatGui.Print(new SeStringBuilder()
                .AddUiForeground("[IPC]", 576)
                .AddText(": Type: ")
                .AddUiForeground(stepData?.InteractionType ?? "?", 61)
                .AddText(" - Pos: ")
                .AddUiForeground(stepData?.Position?.ToString("G", CultureInfo.InvariantCulture) ?? "?", 61)
                .AddText(" - Territory: ")
                .AddUiForeground(stepData?.TerritoryId.ToString(CultureInfo.InvariantCulture) ?? "?", 61)
                .Build());
        }
        else if (arguments == "events")
        {
            List<string> eventQuests = _pluginInterface.GetIpcSubscriber<List<string>>("Questionable.GetCurrentlyActiveEventQuests").InvokeFunc();
            _chatGui.Print(new SeStringBuilder()
                .AddUiForeground("[IPC]", 576)
                .AddText(": Quests: ")
                .AddUiForeground(string.Join(", ", eventQuests), 61)
                .Build());
        }
        else if (arguments == "status")
        {
            string kind = _pluginInterface.GetIpcSubscriber<string>("Questionable.GetStopKind").InvokeFunc();
            bool waiting = _pluginInterface.GetIpcSubscriber<bool>("Questionable.IsWaitingOnPlayer").InvokeFunc();
            string task = _pluginInterface.GetIpcSubscriber<string>("Questionable.GetCurrentTask").InvokeFunc();
            List<string> remaining = _pluginInterface.GetIpcSubscriber<List<string>>("Questionable.GetRemainingTasks").InvokeFunc();
            Print(string.Create(CultureInfo.InvariantCulture,
                $"Stop kind: '{kind}', waiting on you: {waiting}, task: '{task}' (+{remaining.Count})"));
        }
        else
            _chatGui.PrintError("Unknown subcommand");
    }

    // ReSharper disable once InconsistentNaming
    private sealed class IpcStepData
    {
        public required string InteractionType { get; set; }
        public required Vector3? Position { get; set; }
        public required ushort TerritoryId { get; set; }
    }
}
