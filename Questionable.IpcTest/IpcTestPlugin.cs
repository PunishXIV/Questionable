using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;

namespace Questionable.IpcTest;

// ReSharper disable once InconsistentNaming
public sealed class IpcTestPlugin : IDalamudPlugin
{
    //private readonly WindowSystem _windowSystem = new("Questionable/" + nameof(IpcTestPlugin));
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ICommandManager _commandManager;
    private readonly IChatGui _chatGui;

    public IpcTestPlugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IChatGui chatGui)
    {
        _pluginInterface = pluginInterface;
        _commandManager = commandManager;
        _chatGui = chatGui;

        commandManager.AddHandler("/qipc", new CommandInfo(ProcessCommand));
    }

    private void ProcessCommand(string command, string arguments)
    {
        if (arguments == "stepdata")
        {
            var stepData = _pluginInterface.GetIpcSubscriber<IpcStepData?>("Questionable.GetCurrentStepData").InvokeFunc();
            _chatGui.Print(new SeStringBuilder()
                .AddUiForeground("[IPC]", 576)
                .AddText(": Type: ")
                .AddUiForeground(stepData?.InteractionType ?? "?", 61)
                .AddText(" - Pos: ")
                .AddUiForeground(stepData?.Position?.ToString("G", CultureInfo.InvariantCulture) ?? "?", 61)
                .AddText(" - Territory: ")
                .AddUiForeground(stepData?.TerritoryId.ToString() ?? "?", 61)
                .Build());
        }
        else if (arguments == "events")
        {
            var eventQuests = _pluginInterface.GetIpcSubscriber<List<string>>("Questionable.GetCurrentlyActiveEventQuests").InvokeFunc();
            _chatGui.Print(new SeStringBuilder()
                .AddUiForeground("[IPC]", 576)
                .AddText(": Quests: ")
                .AddUiForeground(string.Join(", ", eventQuests), 61)
                .Build());
        }
        else
            _chatGui.PrintError("Unknown subcommand");
    }

    public void Dispose()
    {
        _commandManager.RemoveHandler("/qipc");
    }

    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Local")]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    // ReSharper disable once InconsistentNaming
    private sealed class IpcStepData
    {
        public required string InteractionType { get; set; }
        public required Vector3? Position { get; set; }
        public required ushort TerritoryId { get; set; }
    }
}
