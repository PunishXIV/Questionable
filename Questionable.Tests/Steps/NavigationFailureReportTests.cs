using System.Text.Json;
using Questionable.Controller;
using Questionable.Utils;
using Questionable.Windows.QuestComponents;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class NavigationFailureReportTests
{
    [Fact]
    public void NoFailure_OmitsFailureSection()
    {
        Dictionary<string, object?> report = new() { ["Quest"] = "current quest" };
        QuickAccessComponent.AddNavigationFailure(report, null);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(report, JsonOptions.Default));
        Assert.Equal("current quest", json.RootElement.GetProperty("Quest").GetString());
        Assert.False(json.RootElement.TryGetProperty("LastNavigationFailure", out _));
    }

    [Fact]
    public void PastFailure_KeptApartFromCurrent()
    {
        var failure = JsonSerializer.Deserialize<NavigationFailureSnapshot>("""
            {
              "TimestampUtc": "2026-11-01T12:00:00+00:00",
              "Reason": "TimedOut",
              "Message": "Pathfinding timed out after 30 seconds",
              "ExceptionType": "PathfindingException",
              "InnerExceptionType": "InvalidOperationException",
              "InnerExceptionMessage": "Provider diagnostic",
              "TerritoryId": 958,
              "PlayerPosition": "1.25 2.5 3.75",
              "Destination": "10 20 30",
              "DataId": 1039882,
              "Fly": false,
              "StopDistance": 2,
              "ElapsedMilliseconds": 30000,
              "NavmeshVersion": "1.2.3.14",
              "MovementAllowed": false,
              "SimpleMoveInProgress": false,
              "PathfindInProgress": true,
              "QueuedPathfinds": 0,
              "FloorPoint": "10 19 30",
              "NearestReachablePoint": "10 20 30",
              "Quest": "4387_A Way Forward / 5 / 0",
              "Task": "MoveTo(10, 20, 30)"
            }
            """, JsonOptions.Default)!;
        Dictionary<string, object?> report = new() { ["Quest"] = "a different quest" };
        QuickAccessComponent.AddNavigationFailure(report, failure);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(report, JsonOptions.Default));
        Assert.Equal("a different quest", json.RootElement.GetProperty("Quest").GetString());
        var captured = json.RootElement.GetProperty("LastNavigationFailure");
        Assert.Equal(failure.TimestampUtc, captured.GetProperty("TimestampUtc").GetDateTimeOffset());
        Assert.Equal("4387_A Way Forward / 5 / 0", captured.GetProperty("Quest").GetString());
        Assert.Equal("MoveTo(10, 20, 30)", captured.GetProperty("Task").GetString());
        Assert.Equal("TimedOut", captured.GetProperty("Reason").GetString());
        Assert.Equal("Provider diagnostic", captured.GetProperty("InnerExceptionMessage").GetString());
        Assert.Equal("1.25 2.5 3.75", captured.GetProperty("PlayerPosition").GetString());
        Assert.Equal("10 20 30", captured.GetProperty("Destination").GetString());
        Assert.Equal(958u, captured.GetProperty("TerritoryId").GetUInt32());
        Assert.Equal(30000, captured.GetProperty("ElapsedMilliseconds").GetDouble());
        Assert.Equal("1.2.3.14", captured.GetProperty("NavmeshVersion").GetString());
        Assert.False(captured.GetProperty("Fly").GetBoolean());
        Assert.False(captured.GetProperty("MovementAllowed").GetBoolean());
        Assert.False(captured.GetProperty("SimpleMoveInProgress").GetBoolean());
        Assert.True(captured.GetProperty("PathfindInProgress").GetBoolean());
        Assert.Equal(0, captured.GetProperty("QueuedPathfinds").GetInt32());
        Assert.Equal("10 19 30", captured.GetProperty("FloorPoint").GetString());
        Assert.Equal("10 20 30", captured.GetProperty("NearestReachablePoint").GetString());
    }
}
