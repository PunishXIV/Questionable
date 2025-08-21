using Dalamud.Game.Text.SeStringHandling;

namespace Questionable.Controller.Steps;

internal interface IToastAware : ITaskExecutor
{
    bool OnErrorToast(SeString message);
}
