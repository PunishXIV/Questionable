using System.Text.Json;
using System.Text.Json.Nodes;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class GatheredItemDeserializationTests
{
  [Fact]
  public void MinimalItem_OmittedOptionalFieldsDeserializeToZero()
  {
    const string json = """
        {
          "ItemId": 17945,
          "ItemCount": 20
        }
        """;

    var item = JsonNode.Parse(json)!.Deserialize<GatheredItem>();

    Assert.Equal(17945u, item!.ItemId);
    Assert.Equal(20, item.ItemCount);
    Assert.Equal(0u, item.AlternativeItemId);
    Assert.Equal((ushort)0, item.Collectability);
    Assert.Null(item.FishingOptions);
  }

  [Fact]
  public void WithCollectability_ExplicitValueRoundTrips()
  {
    const string json = """
        {
          "ItemId": 35600,
          "ItemCount": 6,
          "Collectability": 600
        }
        """;

    var item = JsonNode.Parse(json)!.Deserialize<GatheredItem>();

    Assert.Equal(35600u, item!.ItemId);
    Assert.Equal(6, item.ItemCount);
    Assert.Equal((ushort)600, item.Collectability);
    Assert.Equal(0u, item.AlternativeItemId);
  }

  [Fact]
  public void GatherStep_MinimalItemsToGatherFlowThroughFactory()
  {
    const string json = """
        {
          "InteractionType": "Gather",
          "TerritoryId": 612,
          "ItemsToGather": [
            {
              "ItemId": 17945,
              "ItemCount": 20
            }
          ]
        }
        """;

    var step = JsonNode.Parse(json)!.Deserialize<QuestStep>();
    var (quest, sequence, _) = QuestTestData.FactoryContext(new QuestId(2621), 4, step!);
    var factory = new Gather.Factory();

    var task = factory.CreateAllTasks(quest, sequence, step!).OfType<Gather.DelayedGatheringTask>().Single();

    Assert.Equal(17945u, task.GatheredItem.ItemId);
    Assert.Equal(20, task.GatheredItem.ItemCount);
    Assert.Equal(0u, task.GatheredItem.AlternativeItemId);
    Assert.Equal((ushort)0, task.GatheredItem.Collectability);
  }
}
