using static Questionable.Controller.Steps.Shared.Fish;

namespace Questionable.Controller.Steps.Fishing;

internal interface IFishingPresetGenerator
{
    /// <summary>
    /// Creates an AutoHook fishing preset from a fish task.
    /// </summary>
    /// <param name="task">The fish task to create a preset from.</param>
    /// <returns>The AutoHotkey fishing preset. Gzipped and base64 encoded.</returns>
    string CreatePresetFromTask(FishTask task);
}
