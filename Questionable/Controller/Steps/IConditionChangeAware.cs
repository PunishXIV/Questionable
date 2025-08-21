using Dalamud.Game.ClientState.Conditions;

namespace Questionable.Controller.Steps;

internal interface IConditionChangeAware : ITaskExecutor
{
    void OnConditionChange(ConditionFlag flag, bool value);
}
