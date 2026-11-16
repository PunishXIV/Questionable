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

        // No controller/starter is installed: any attempt to select a quest or activate the MSQ fallback fails this test.
        object? result = typeof(QuestionableIpc).GetMethod("StartQuest", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ipc, ["435", true]);

        Assert.Equal(false, result);
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}
