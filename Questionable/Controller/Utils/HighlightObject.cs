using System.Runtime.CompilerServices;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel.Sheets;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace Questionable.Controller.Utils;

// Adapted from https://github.com/electr0sheep/ItemVendorLocation/blob/main/ItemVendorLocation/HighlightObject.cs
[RegisterSingleton]
internal sealed class HighlightObject : IDisposable
{
    private readonly ICondition _condition;
    private readonly Configuration _configuration;
    private readonly IDataManager _dataManager;
    private readonly IFramework _framework;
    private readonly ILogger<HighlightObject> _logger;
    private readonly IObjectTable _objectTable;
    private DateTime _lastUpdateTime = DateTime.Now;
    private uint[] _targetDataIds = [];

    public HighlightObject(
        IFramework framework,
        Configuration configuration,
        ICondition condition,
        IObjectTable objectTable,
        IDataManager dataManager,
        ILogger<HighlightObject> logger)
    {
        _framework = framework;
        _configuration = configuration;
        _condition = condition;
        _objectTable = objectTable;
        _dataManager = dataManager;
        _logger = logger;
        _framework.Update += OnFrameworkUpdate;
    }

    public void Dispose() => _framework.Update -= OnFrameworkUpdate;

    private void OnFrameworkUpdate(IFramework framework)
    {
        //we want to update every 300 ms
        if (DateTime.Now - _lastUpdateTime <= TimeSpan.FromMilliseconds(300))
            return;

        _lastUpdateTime = DateTime.Now;

        if (!_configuration.Advanced.HighlightSelectedNpc || _targetDataIds.Length == 0)
            return;

        if (_condition[ConditionFlag.Occupied] || _condition[ConditionFlag.Occupied30] ||
            _condition[ConditionFlag.Occupied33] || _condition[ConditionFlag.Occupied38] ||
            _condition[ConditionFlag.Occupied39] || _condition[ConditionFlag.OccupiedInEvent] ||
            _condition[ConditionFlag.OccupiedInQuestEvent] || _condition[ConditionFlag.OccupiedInCutSceneEvent] ||
            _condition[ConditionFlag.Casting] || _condition[ConditionFlag.MountOrOrnamentTransition] ||
            _condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51] ||
            _condition[ConditionFlag.Mounting71])
            ToggleHighlight(on: false);
        else
            ToggleHighlight(on: true);
    }

    public void AddHighlight(uint id,
                   [CallerFilePath] string file = "",
                 [CallerLineNumber] int line = 0)
    {
        _ = _framework.Run(() =>
        {
            if (!_targetDataIds.Contains(id))
            {
                _logger.LogDebug("Adding {Id} to highlight ({Caller})", id, $"{Path.GetFileName(file)}:{line}");
                _targetDataIds = _targetDataIds.Append(id).ToArray();
            }
        });
    }

    public void SetHighlight(uint[] ids,
                   [CallerFilePath] string file = "",
                 [CallerLineNumber] int line = 0)
    {
        _ = _framework.Run(() =>
        {
            ToggleHighlight(on: false);
            if (_targetDataIds.Length == 0 && ids.Length == 0)
                return;
            _logger.LogDebug("Setting highlight to {Ids} ({Caller})", string.Join(',', ids), $"{Path.GetFileName(file)}:{line}");
            _targetDataIds = ids;
            ToggleHighlight(on: true);
        });
    }

    public unsafe void ToggleHighlight(bool on)
    {
        if (_targetDataIds.All(n => n == 0))
            return;

        IGameObject[] gameObjects = _objectTable.Where(i =>
        {
            if (!i.IsValid())
                return false;
            GameObject* obj = (GameObject*)i.Address;
            return _targetDataIds.Contains(obj->BaseId);
        }).ToArray();

        if (gameObjects.Length == 0)
            return;

        foreach (IGameObject obj in gameObjects)
            ((GameObject*)obj.Address)->Highlight(on ? _configuration.Advanced.HighlightColor : ObjectHighlightColor.None);
    }
}
