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

    [Fact]
    public void RewardTotals_SkipQuestsDone()
    {
        QuestPriorityManager manager = CreateManager(id => TestQuestInfo.Create(id, $"Q{id}", 1)
            .With(nameof(QuestInfo.Experience), (uint)id * 10)
            .With(nameof(QuestInfo.Gil), (uint)id));
        manager.Add(new QuestId(First));
        manager.Add(new QuestId(Second));
        manager.Add(new QuestId(Third));

        Assert.Equal((10_020UL, 1_002UL), manager.GetRewardTotals(x => x.Value == Second));
        Assert.Equal((0UL, 0UL), manager.GetRewardTotals(_ => true));
    }

    private static QuestPriorityManager CreateManager(Func<ushort, QuestInfo>? createInfo = null)
    {
        FakeQuestCatalog catalog = new();
        Dictionary<ElementId, Quest> quests = [];
        foreach (ushort id in new[] { First, Second, Third })
        {
            QuestInfo info = createInfo?.Invoke(id) ?? TestQuestInfo.Create(id, $"Q{id}", 1);
            Quest quest = catalog.Add(info, QuestTestData.Seq(0, new QuestStep()));
            quests[quest.Id] = quest;
        }

        var registry = (QuestRegistry)RuntimeHelpers.GetUninitializedObject(typeof(QuestRegistry));
        typeof(QuestRegistry).GetField("_quests", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(registry, quests);
        return new QuestPriorityManager(registry, NullLogger<QuestPriorityManager>.Instance, Substitute.For<IChatGui>());
    }
}
