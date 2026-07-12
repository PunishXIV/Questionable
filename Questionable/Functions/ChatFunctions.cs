using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.Automation;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;
using Questionable.Model.Questing;

namespace Questionable.Functions;

internal sealed class ChatFunctions
(
    IDataManager dataManager,
    GameFunctions gameFunctions,
    ITargetManager targetManager,
    ILogger<ChatFunctions> logger)
{
    private readonly ReadOnlyDictionary<EEmote, string> _emoteCommands = dataManager.GetExcelSheet<Emote>()
        .Where(x => x.RowId > 0 && x.TextCommand.IsValid)
        .Select(x => (x.RowId, Command: x.TextCommand.Value.Command.ToString()))
        .Where(x => !string.IsNullOrEmpty(x.Command) && x.Command.StartsWith('/'))
        .ToDictionary(x => (EEmote)x.RowId, x => x.Command)
        .AsReadOnly();

    private readonly GameFunctions _gameFunctions = gameFunctions;
    private readonly ITargetManager _targetManager = targetManager;
    private readonly ILogger<ChatFunctions> _logger = logger;

    public void ExecuteCommand(string command)
    {
        if (!command.StartsWith('/'))
            return;

        _logger.LogDebug("Executing chat command '{Command}'", command);
        Chat.ExecuteCommand(command);
    }

    public bool UseEmote(uint dataId, EEmote emote)
    {
        IGameObject? gameObject = _gameFunctions.FindObjectByDataId(dataId);
        if (gameObject != null)
        {
            _targetManager.Target = gameObject;
            ExecuteCommand($"{_emoteCommands[emote]} motion");
            return true;
        }

        _logger.LogWarning("Could not find object with DataId {DataId} for emote {Emote}", dataId, emote);
        return false;
    }

    public void UseEmote(EEmote emote) => ExecuteCommand($"{_emoteCommands[emote]} motion");
}
