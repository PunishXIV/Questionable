using ECommons.ExcelServices;
using Questionable.Data;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class ClassJobUtilsTests
{
    [Theory]
    [InlineData(Job.ACN, Job.SMN, Job.SMN)]
    [InlineData(Job.ACN, Job.SCH, Job.SCH)]
    [InlineData(Job.ACN, Job.ADV, Job.SMN)]
    [InlineData(Job.MRD, Job.SCH, Job.WAR)]
    [InlineData(Job.WAR, Job.SCH, Job.WAR)]
    public void FollowUpJob_ArcanistSetting(Job classJob, Job arcanistJob, Job expected) =>
        Assert.Equal(expected, ClassJobUtils.FollowUpJob(classJob, arcanistJob));

    [Theory]
    [InlineData(Job.ACN, Job.SMN, Job.SMN, 4549)]
    [InlineData(Job.ACN, Job.SCH, Job.SCH, 4550)]
    [InlineData(Job.MRD, Job.SCH, Job.WAR, 4544)]
    public void JobStoneFor_ArcanistSetting(Job classJob, Job arcanistJob, Job job, int item) =>
        Assert.Equal((job, (ushort)item), ClassJobUtils.JobStoneFor(classJob, arcanistJob));

    [Theory]
    [InlineData(Job.SCH)]
    [InlineData(Job.SMN)]
    [InlineData(Job.WAR)]
    public void JobStoneFor_JobHasNone(Job job) =>
        Assert.Null(ClassJobUtils.JobStoneFor(job, Job.SCH));
}
