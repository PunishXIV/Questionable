using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;
using Questionable.Model.Questing;
using System;
using System.Linq;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace Questionable.Controller.Utils;

// Adapted from https://github.com/electr0sheep/ItemVendorLocation/blob/main/ItemVendorLocation/HighlightObject.cs
internal sealed class HighlightObject : IDisposable
{
    private readonly ICondition _condition;
    private readonly Configuration _configuration;
    private readonly IDataManager _dataManager;
    private readonly IFramework _framework;
    private readonly ILogger<HighlightObject> _logger;
    private readonly IObjectTable _objectTable;
    private DateTime _lastUpdateTime = DateTime.Now;
    private uint[] _targetNpcDataId = [];

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
        _framework.Update += Framework_OnUpdate;
    }

    public void Dispose()
    {
        _framework.Update -= Framework_OnUpdate;
    }

    private void Framework_OnUpdate(IFramework framework)
    {
        //we want to update every 300 ms
        if (DateTime.Now - _lastUpdateTime <= TimeSpan.FromMilliseconds(300))
        {
            return;
        }

        _lastUpdateTime = DateTime.Now;

        if (!_configuration.Advanced.HighlightSelectedNpc || _targetNpcDataId.Length == 0)
        {
            return;
        }

        if (_condition[ConditionFlag.Occupied] || _condition[ConditionFlag.Occupied30] ||
            _condition[ConditionFlag.Occupied33] || _condition[ConditionFlag.Occupied38] ||
            _condition[ConditionFlag.Occupied39] || _condition[ConditionFlag.OccupiedInEvent] ||
            _condition[ConditionFlag.OccupiedInQuestEvent] || _condition[ConditionFlag.OccupiedInCutSceneEvent] ||
            _condition[ConditionFlag.Casting] || _condition[ConditionFlag.MountOrOrnamentTransition] ||
            _condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51])
        {
            ToggleHighlight(false);
        }
        else
        {
            ToggleHighlight(true);
        }
    }

    public void AddHighlight(uint Id)
    {
        _ = _framework.Run(() =>
        {
            if (!_targetNpcDataId.Contains(Id))
            {
                _logger.LogDebug($"Adding {Id} to highlight");
                _targetNpcDataId = _targetNpcDataId.Append(Id).ToArray();
            }
        });
    }

    public void RemoveHighlight(uint Id)
    {
        _ = _framework.Run(() =>
        {
            _logger.LogDebug($"Removing {Id} from highlight");
            _targetNpcDataId = _targetNpcDataId.Where(n => n != Id).ToArray();
        });
    }

    public void HighlightQuestObjects(ElementId questId)
    {
        SetHighlight(_dataManager.GetExcelSheet<EObj>().Where(obj => obj.Data.Equals((uint)questId.Value)).Select(obj => obj.RowId).ToArray());
    }

    public void SetHighlight(uint[] Ids)
    {
        _ = _framework.Run(() =>
        {
            ToggleHighlight(false);
            if (_targetNpcDataId.Length == 0 && Ids.Length == 0)
            {
                return;
            }
            _logger.LogDebug($"Setting highlight to {string.Join(',', Ids)}");
            _targetNpcDataId = Ids;
            ToggleHighlight(true);
        });
    }

    public unsafe void ToggleHighlight(bool on)
    {
        if (_targetNpcDataId.All(n => n == 0))
        {
            return;
        }

        IGameObject[] gameObjects = _objectTable.Where(i =>
        {
            if (!i.IsValid())
            {
                return false;
            }
            GameObject* obj = (GameObject*)i.Address;
            return _targetNpcDataId.Contains(obj->BaseId);
        }).ToArray();

        if (gameObjects.Length == 0)
        {
            return;
        }

        foreach(IGameObject obj in gameObjects)
        {
            ((GameObject*)obj.Address)->Highlight(on ? _configuration.Advanced.HighlightColor : ObjectHighlightColor.None);
        }
    }
}
