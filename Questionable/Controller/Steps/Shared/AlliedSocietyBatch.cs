using Questionable.Model;
using Quest = Questionable.Model.Quest;

namespace Questionable.Controller.Steps.Shared;

internal static class AlliedSocietyBatch
{
    internal static bool SupportsBatch(Quest quest) =>
        quest.Info is { AlliedSociety: not (EAlliedSociety.None or EAlliedSociety.Ixal), IsRepeatable: true };
}
