using Microsoft.Extensions.Logging;
using Questionable.Model;
using Questionable.Model.Questing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Questionable.Controller.Utils;

internal static class QuestWorkUtils
{
    public static bool HasCompletionFlags(IList<QuestWorkValue?> completionQuestVariablesFlags)
    {
        return completionQuestVariablesFlags.Count == 6 && completionQuestVariablesFlags.Any(x => x != null && (x.High != 0 || x.Low != 0));
    }

    public static bool MatchesQuestWork(IList<QuestWorkValue?> completionQuestVariablesFlags, QuestProgressInfo questProgressInfo)
    {
        if (!HasCompletionFlags(completionQuestVariablesFlags) || questProgressInfo.Variables.Count != 6)
            return false;

        for (int i = 0; i < questProgressInfo.Variables.Count; ++i)
        {
            QuestWorkValue? check = completionQuestVariablesFlags[i];
            if (check == null)
                continue;

            EQuestWorkMode mode = check.Mode;

            byte actualHigh = (byte)(questProgressInfo.Variables[i] >> 4);
            byte actualLow = (byte)(questProgressInfo.Variables[i] & 0xF);

            byte? checkHigh = check.High;
            byte? checkLow = check.Low;

            byte expectedHigh = checkHigh.GetValueOrDefault();
            byte expectedLow = checkLow.GetValueOrDefault();
            if (mode == EQuestWorkMode.Exact)
            {
                if (checkHigh != null && actualHigh != expectedHigh)
                    return false;

                if (checkLow != null && actualLow != expectedLow)
                    return false;
            }
            else if (mode == EQuestWorkMode.Bitwise)
            {
                if (checkHigh != null && (actualHigh & checkHigh) != expectedHigh)
                    return false;

                if (checkLow != null && (actualLow & checkLow) != expectedLow)
                    return false;
            }
            else
                throw new InvalidOperationException($"Unknown qw mode {mode}");
        }

        return true;
    }

    public static bool MatchesRequiredQuestWorkConfig(List<List<QuestWorkValue>?> requiredQuestVariables,
        QuestProgressInfo questWork, ILogger logger)
    {
        if (requiredQuestVariables.Count != 6 || requiredQuestVariables.All(x => x == null || x.Count == 0))
        {
            logger.LogDebug("No RequiredQW defined");
            return true;
        }

        for (int i = 0; i < 6; ++i)
        {
            if (requiredQuestVariables[i] == null)
            {
                logger.LogDebug("No RequiredQW {Index} defined", i);
                continue;
            }

            byte high = (byte)(questWork.Variables[i] >> 4);
            byte low = (byte)(questWork.Variables[i] & 0xF);

            foreach (QuestWorkValue expectedValue in requiredQuestVariables[i]!)
            {
                logger.LogDebug("H: {ExpectedHigh} - {ActualHigh}, L: {ExpectedLow} - {ActualLow}",
                    expectedValue.High, high, expectedValue.Low, low);
                if (expectedValue.High != null && expectedValue.High != high)
                    continue;

                if (expectedValue.Low != null && expectedValue.Low != low)
                    continue;

                return true;
            }
        }

        logger.LogInformation("Should execute step");
        return false;
    }
}
