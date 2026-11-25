// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu
using System.Reflection;
using System.Runtime.CompilerServices;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Data;
using Questionable.Domain;
using Questionable.External;
using Questionable.Functions;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class QuestionableIpcTests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void UnavailableSingle_DoesNotStart(bool complete, bool locked, bool lowLevel)
    {
        var state = new FakeQuestGameState { CurrentLevel = (short)(lowLevel ? 1 : 100) };
        var catalog = new FakeQuestCatalog();
        catalog.Add(TestQuestInfo.Create(434));
        Quest quest = catalog.Add(TestQuestInfo.Create(435, level: 15,
            previous: locked ? [new(new QuestId(434))] : []));
        if (complete)
            state.Complete.Add(435);
        var functions = new QuestFunctions(catalog, state, new AlliedSocietyData(), new Configuration(), Substitute.For<IChatGui>());
        var registry = (QuestRegistry)RuntimeHelpers.GetUninitializedObject(typeof(QuestRegistry));
        SetField(registry, "_quests", new Dictionary<ElementId, Quest> { [quest.Id] = quest });

        var ipc = (QuestionableIpc)RuntimeHelpers.GetUninitializedObject(typeof(QuestionableIpc));
        SetField(ipc, "_logger", NullLogger<QuestionableIpc>.Instance);
        SetField(ipc, "_questRegistry", registry);
        SetField(ipc, "_questFunctions", functions);

        object? result = typeof(QuestionableIpc).GetMethod("StartQuest", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ipc, ["435", true]);

        Assert.Equal(false, result);
    }

    [Theory]
    [InlineData("AddQuestPriority", "abc")]
    [InlineData("AddQuestPriority", "9999")]
    [InlineData("InsertQuestPriority", "abc")]
    [InlineData("InsertQuestPriority", "9999")]
    public void UnknownQuest_IsNotAdded(string method, string questId)
    {
        var registry = (QuestRegistry)RuntimeHelpers.GetUninitializedObject(typeof(QuestRegistry));
        SetField(registry, "_quests", new Dictionary<ElementId, Quest>());
        var ipc = (QuestionableIpc)RuntimeHelpers.GetUninitializedObject(typeof(QuestionableIpc));
        SetField(ipc, "_logger", NullLogger<QuestionableIpc>.Instance);
        SetField(ipc, "_questRegistry", registry);

        object?[] args = method == "AddQuestPriority" ? [questId] : [0, questId];
        object? result = typeof(QuestionableIpc).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ipc, args);

        Assert.Equal(false, result);
    }

    [Fact]
    public void EventQuests_PickFirstStartable()
    {
        var catalog = new FakeQuestCatalog();
        Quest done = catalog.Add(TestQuestInfo.Create(4796));
        Quest next = catalog.Add(TestQuestInfo.Create(4797));
        Quest later = catalog.Add(TestQuestInfo.Create(4798));
        var paths = new Dictionary<ElementId, Quest> { [next.Id] = next, [later.Id] = later };
        ElementId noPath = new QuestId(4795);

        Quest? picked = QuestionableIpc.NextEventQuest([noPath, done.Id, next.Id, later.Id],
            id => paths.GetValueOrDefault(id), _ => false, id => !id.Equals(done.Id));

        Assert.Same(next, picked);
        Assert.Null(QuestionableIpc.NextEventQuest([done.Id], id => paths.GetValueOrDefault(id), _ => false, _ => false));
    }

    [Fact]
    public void Societies_ParseByName()
    {
        List<EAlliedSociety>? parsed = QuestionableIpc.ParseSocieties(["ixal", "Kobolds"]);
        Assert.Equal([EAlliedSociety.Ixal, EAlliedSociety.Kobolds], parsed);
    }

    [Theory]
    [InlineData("Moogle")]
    [InlineData("None")]
    [InlineData("999")]
    public void UnknownSociety_FailsWholeList(string name) =>
        Assert.Null(QuestionableIpc.ParseSocieties(["Ixal", name]));

    [Fact]
    public void StepChanged_OnlyOnChange()
    {
        (ElementId, byte, int) step = (new QuestId(4797), 3, 0);
        Assert.False(QuestionableIpc.StepChanged(step, (new QuestId(4797), 3, 0)));
        Assert.True(QuestionableIpc.StepChanged(step, (new QuestId(4797), 3, 1)));
        Assert.True(QuestionableIpc.StepChanged(null, step));
        Assert.False(QuestionableIpc.StepChanged(null, null));
    }

    [Theory]
    [InlineData("4797", true)]
    [InlineData("9999", false)]
    [InlineData("abc", false)]
    public void HasQuestPath_KnownQuestsOnly(string questId, bool expected)
    {
        var catalog = new FakeQuestCatalog();
        Quest quest = catalog.Add(TestQuestInfo.Create(4797));
        var registry = (QuestRegistry)RuntimeHelpers.GetUninitializedObject(typeof(QuestRegistry));
        SetField(registry, "_quests", new Dictionary<ElementId, Quest> { [quest.Id] = quest });
        var ipc = (QuestionableIpc)RuntimeHelpers.GetUninitializedObject(typeof(QuestionableIpc));
        SetField(ipc, "_logger", NullLogger<QuestionableIpc>.Instance);
        SetField(ipc, "_questRegistry", registry);

        object? result = typeof(QuestionableIpc).GetMethod("HasQuestPath", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ipc, [questId]);

        Assert.Equal(expected, result);
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}
