using System.Reflection;
using System.Runtime.CompilerServices;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;
using Quest = Questionable.Domain.Quest;

namespace Questionable.Tests.Steps;

public sealed class QuestPriorityManagerTests
{
    private const ushort First = 500, Second = 501, Third = 502;

    [Fact]
    public void ClearAllAcceptOnly_KeepsQuestsInList()
    {
        QuestPriorityManager manager = CreateManager();
        manager.MarkAcceptOnly(new QuestId(First));
        manager.MarkAcceptOnly(new QuestId(Second));
        manager.Add(new QuestId(Third));

        manager.ClearAllAcceptOnly();

        Assert.False(manager.HasPendingAcceptOnly);
        Assert.Empty(manager.AcceptOnlyQuests);
        Assert.Equal([First, Second, Third], manager.Quests.Select(x => x.Id.Value));
    }

    private static QuestPriorityManager CreateManager()
    {
        FakeQuestCatalog catalog = new();
        Dictionary<ElementId, Quest> quests = [];
        foreach (ushort id in new[] { First, Second, Third })
        {
            Quest quest = catalog.Add(TestQuestInfo.Create(id, $"Q{id}", 1), QuestTestData.Seq(0, new QuestStep()));
            quests[quest.Id] = quest;
        }

        var registry = (QuestRegistry)RuntimeHelpers.GetUninitializedObject(typeof(QuestRegistry));
        typeof(QuestRegistry).GetField("_quests", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(registry, quests);
        return new QuestPriorityManager(registry, NullLogger<QuestPriorityManager>.Instance, Substitute.For<IChatGui>());
    }
}
