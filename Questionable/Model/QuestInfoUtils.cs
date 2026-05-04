using ECommons.ExcelServices;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
namespace Questionable.Model;

internal static class QuestInfoUtils
{
    private static readonly Dictionary<uint, IReadOnlyList<Job>> CachedClassJobs = [];

    internal static IReadOnlyList<Job> AsList(ClassJobCategory? optionalClassJobCategory)
    {
        if (optionalClassJobCategory == null)
        {
            return Enum.GetValues<Job>();
        }

        ClassJobCategory classJobCategory = optionalClassJobCategory.Value;
        if (CachedClassJobs.TryGetValue(classJobCategory.RowId, out IReadOnlyList<Job>? classJobs))
        {
            return classJobs;
        }

        classJobs = new Dictionary<Job, bool>
            {
                { Job.ADV, classJobCategory.ADV },
                { Job.GLA, classJobCategory.GLA },
                { Job.PGL, classJobCategory.PGL },
                { Job.MRD, classJobCategory.MRD },
                { Job.LNC, classJobCategory.LNC },
                { Job.ARC, classJobCategory.ARC },
                { Job.CNJ, classJobCategory.CNJ },
                { Job.THM, classJobCategory.THM },
                { Job.CRP, classJobCategory.CRP },
                { Job.BSM, classJobCategory.BSM },
                { Job.ARM, classJobCategory.ARM },
                { Job.GSM, classJobCategory.GSM },
                { Job.LTW, classJobCategory.LTW },
                { Job.WVR, classJobCategory.WVR },
                { Job.ALC, classJobCategory.ALC },
                { Job.CUL, classJobCategory.CUL },
                { Job.MIN, classJobCategory.MIN },
                { Job.BTN, classJobCategory.BTN },
                { Job.FSH, classJobCategory.FSH },
                { Job.PLD, classJobCategory.PLD },
                { Job.MNK, classJobCategory.MNK },
                { Job.WAR, classJobCategory.WAR },
                { Job.DRG, classJobCategory.DRG },
                { Job.BRD, classJobCategory.BRD },
                { Job.WHM, classJobCategory.WHM },
                { Job.BLM, classJobCategory.BLM },
                { Job.ACN, classJobCategory.ACN },
                { Job.SMN, classJobCategory.SMN },
                { Job.SCH, classJobCategory.SCH },
                { Job.ROG, classJobCategory.ROG },
                { Job.NIN, classJobCategory.NIN },
                { Job.MCH, classJobCategory.MCH },
                { Job.DRK, classJobCategory.DRK },
                { Job.AST, classJobCategory.AST },
                { Job.SAM, classJobCategory.SAM },
                { Job.RDM, classJobCategory.RDM },
                { Job.BLU, classJobCategory.BLU },
                { Job.GNB, classJobCategory.GNB },
                { Job.DNC, classJobCategory.DNC },
                { Job.RPR, classJobCategory.RPR },
                { Job.SGE, classJobCategory.SGE },
                { Job.VPR, classJobCategory.VPR },
                { Job.PCT, classJobCategory.PCT }
            }
            .Where(y => y.Value)
            .Select(y => y.Key)
            .ToList()
            .AsReadOnly();
        CachedClassJobs[classJobCategory.RowId] = classJobs;
        return classJobs;
    }
}
