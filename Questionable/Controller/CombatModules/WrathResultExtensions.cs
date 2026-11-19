using WrathCombo.API.Enum;

namespace Questionable.Controller.CombatModules;

internal static class WrathResultExtensions
{
    public static bool AllSuccessful
    (out string failedVariableNames,
        params (string name, SetResult result)[] results)
    {
        string[] failed = results
            .Where(r => !r.result.IsSuccess())
            .Select(r => r.name)
            .ToArray();

        failedVariableNames = string.Join(", ", failed);
        return failed.Length == 0;
    }

    public static bool IsSuccess(this SetResult result) => result is SetResult.Okay or SetResult.OkayWorking;
}
