using System.Reflection;
using System.Runtime.CompilerServices;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Domain;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;
using Quest = Questionable.Domain.Quest;

namespace Questionable.Tests.Steps;

public sealed class StopPresetTests
{
    private const ushort Stop = 500, First = 501, Second = 502;

    private readonly Configuration _configuration = new();
    private readonly Dictionary<ElementId, Quest> _quests = [];
    private readonly QuestPriorityManager _priority;
    private readonly QuestController _controller;
    private readonly IChatGui _chat = Substitute.For<IChatGui>();
    private int _saves;

    public StopPresetTests()
    {
        FakeQuestCatalog catalog = new();
        foreach (ushort id in new[] { Stop, First, Second })
        {
            Quest quest = catalog.Add(TestQuestInfo.Create(id, $"Q{id}", 1), QuestTestData.Seq(0, new QuestStep()));
            _quests[quest.Id] = quest;
        }

        var registry = (QuestRegistry)RuntimeHelpers.GetUninitializedObject(typeof(QuestRegistry));
        typeof(QuestRegistry).GetField("_quests", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(registry, _quests);
        _priority = new QuestPriorityManager(registry, NullLogger<QuestPriorityManager>.Instance, Substitute.For<IChatGui>());

        _configuration.Priority.Presets["Mine"] = [new QuestId(First).ToString(), new QuestId(Second).ToString()];
        _configuration.Stop.QuestsToStopAfter.Add(new QuestId(Stop));
        _controller = TestQuestController.Create(configuration: _configuration,
            stopPolicy: new QuestStopPolicy(_configuration, _ => { }, _ => { }, _ => { }, _chat,
                NullLogger<QuestStopPolicy>.Instance, () => _saves++));
        TestQuestController.Set(_controller, "_priorityManager", _priority);
        TestQuestController.Set(_controller, "_presets", new PriorityPresets(_configuration, null!, null!, null!));
    }

    private bool TryLoad() => (bool)typeof(QuestController)
        .GetMethod("TryLoadPreset", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(_controller, [_quests[new QuestId(Stop)]])!;

    [Fact]
    public void NoPreset_Stops()
    {
        Assert.False(TryLoad());
        Assert.True(_priority.IsEmpty);
    }

    [Theory]
    [InlineData(false, "Completed quest 'Q500', continuing with preset 'Mine'.")]
    [InlineData(true, "Completed quest 'Q500', preset 'Mine' is queued for the next start.")]
    public void Preset_AddsToEndOfList(bool stopArmed, string expected)
    {
        _configuration.Stop.SetPreset(new QuestId(Stop), PriorityPresets.CustomPrefix + "Mine");
        _priority.Add(new QuestId(Second));
        _controller.StopAfterCurrentQuest = stopArmed;

        Assert.True(TryLoad());

        Assert.Equal([Second, First], _priority.Quests.Select(x => x.Id.Value));
        Assert.Equal([expected], _chat.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IChatGui.Print))
            .Select(call => (string)call.GetArguments()[0]!));
        Assert.Contains(new QuestId(Stop), _configuration.Stop.QuestsToStopAfter);
    }

    [Fact]
    public void LoadedPreset_ClearsFinishedQuest()
    {
        _configuration.Stop.Enabled = true;
        _configuration.Stop.SetPreset(new QuestId(Stop), PriorityPresets.CustomPrefix + "Mine");
        IQuestSelectionState state = Substitute.For<IQuestSelectionState>();
        state.IsComplete(Arg.Any<ElementId>()).Returns(true);
        TestQuestController.Set(_controller, "_selectionState", state);
        typeof(QuestController).GetProperty(nameof(QuestController.StartedQuest))!
            .SetValue(_controller, new QuestController.QuestProgress(_quests[new QuestId(Stop)], 0));

        bool stopped = (bool)typeof(QuestController)
            .GetMethod("TryStopAtStopPoint", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(_controller, [])!;

        Assert.False(stopped);
        Assert.Null(_controller.StartedQuest);
        Assert.Equal([First, Second], _priority.Quests.Select(x => x.Id.Value));
    }

    [Fact]
    public void MissingPreset_Stops()
    {
        _configuration.Stop.SetPreset(new QuestId(Stop), PriorityPresets.CustomPrefix + "Gone");

        Assert.False(TryLoad());

        Assert.True(_priority.IsEmpty);
        Assert.Single(_chat.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(IChatGui.PrintError));
    }

    [Fact]
    public void RemoveSetting_ClearsQuestAndPreset()
    {
        _configuration.Stop.RemoveWhenCompleteConditionMet = true;
        _configuration.Stop.SetPreset(new QuestId(Stop), PriorityPresets.CustomPrefix + "Mine");

        Assert.True(TryLoad());

        Assert.Empty(_configuration.Stop.QuestsToStopAfter);
        Assert.Empty(_configuration.Stop.PresetsAfterQuest);
        Assert.Equal(1, _saves);
    }
}
