using System.Globalization;
using Microsoft.Extensions.Logging;
using Questionable.Utils;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class TroubleshootingReportTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private static RecentLog.Entry Line(int secondsAgo, string message, LogLevel level = LogLevel.Information) =>
        new(Now.AddSeconds(-secondsAgo), level, message);

    [Fact]
    public void LongLog_DropsOldestAndFitsMessage()
    {
        List<RecentLog.Entry> log = Enumerable.Range(0, 40)
            .Select(i => Line(40 - i, string.Create(CultureInfo.InvariantCulture, $"line {i:D2} ") + new string('x', 150)))
            .ToList();

        string report = TroubleshootingReport.Build(new() { Facts = ["Quest: none"], Log = log, NowUtc = Now });

        Assert.True(report.Length <= TroubleshootingReport.MaxLength, $"Length was {report.Length}");
        Assert.Contains("line 39", report, StringComparison.Ordinal);
        Assert.DoesNotContain("line 00", report, StringComparison.Ordinal);
        Assert.True(report.IndexOf("line 38", StringComparison.Ordinal) < report.IndexOf("line 39", StringComparison.Ordinal));
    }

    [Fact]
    public void HugeFacts_StillFitMessage()
    {
        string[] facts = Enumerable.Range(0, 30).Select(_ => new string('f', 500)).ToArray();
        string[] settings = Enumerable.Range(0, 200).Select(i => string.Create(CultureInfo.InvariantCulture, $"General.Setting{i}=on")).ToArray();

        string report = TroubleshootingReport.Build(new() { Facts = facts, Settings = settings, NowUtc = Now });

        Assert.True(report.Length <= TroubleshootingReport.MaxLength, $"Length was {report.Length}");
        Assert.EndsWith("```", report, StringComparison.Ordinal);
    }

    [Fact]
    public void PrivateTexts_AreHiddenEverywhere()
    {
        string report = TroubleshootingReport.Build(new()
        {
            Facts = ["Hello from Some Player"],
            Settings = ["General.Thing=some player"],
            Log = [Line(1, @"Could not read C:\Users\kage\file.json for SOME PLAYER")],
            PrivateTexts = ["Some Player", @"C:\Users\kage"],
            NowUtc = Now,
        });

        Assert.DoesNotContain("some player", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\Users\kage", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(hidden)", report, StringComparison.Ordinal);
    }

    [Fact]
    public void LogText_CannotBreakCodeBlock()
    {
        string report = TroubleshootingReport.Build(new() { Log = [Line(1, "before ``` after\nsecond line")], NowUtc = Now });

        Assert.Equal(2, report.Split("```").Length - 1);
        Assert.DoesNotContain("second line", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangedSettings_HideFreeText()
    {
        Configuration configuration = new();
        configuration.General.ChocoboName = "Secret Bird";
        configuration.General.DisplayName = "Real Name";
        configuration.General.MountId = 5;
        configuration.Advanced.Debug = true;
        configuration.Duties.WhitelistedDutyCfcIds.Add(1);
        configuration.CharacterProfiles["18014498565000000"] = "Main";

        List<string> changed = TroubleshootingReport.ChangedSettings(configuration);
        string joined = string.Join(',', changed);

        Assert.Contains("General.MountId=5", changed);
        Assert.Contains("Advanced.Debug=on", changed);
        Assert.Contains("Duties.WhitelistedDutyCfcIds=(1 items)", changed);
        Assert.Contains("General.ChocoboName=(text)", changed);
        Assert.DoesNotContain("Secret Bird", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("Real Name", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("18014498565000000", joined, StringComparison.Ordinal);
    }

    [Fact]
    public void RecentLog_KeepsNewestAndSkipsDebug()
    {
        using RecentLog log = new();
        ILogger logger = log.CreateLogger("test");

        logger.LogDebug("hidden");
        for (int i = 0; i < 50; ++i)
            logger.LogInformation("line {Index}", i);
        logger.LogError(new InvalidOperationException("bad"), "failed");

        IReadOnlyList<RecentLog.Entry> entries = log.GetEntries();
        Assert.Equal(40, entries.Count);
        Assert.Equal("line 11", entries[0].Message);
        Assert.Equal("failed | InvalidOperationException: bad", entries[^1].Message);
        Assert.Equal(LogLevel.Error, entries[^1].Level);
    }
}
