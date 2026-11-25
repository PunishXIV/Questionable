// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu
using System.Reflection;
using System.Runtime.CompilerServices;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Questionable.Controller;
using Questionable.Controller.GameUi;
using Questionable.Data;
using Questionable.Functions;
using Questionable.Model.Questing;
using Questionable.Tests.AutoGen;
using Questionable.Tests.TestData;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class DialogueChoiceHandlerTests
{
    [GameDataFact]
    public void GoldSaucerMenu_MatchesWarp()
    {
        NoPluginsInstalled.Ensure();
        string? directory = GameDataFixture.ResolveSqPackDirectory(
            Environment.GetEnvironmentVariable(GameDataFixture.GamePathEnvVar), GameDataFixture.FindInstalledSqPackDirectory);
        using var data = new Lumina.GameData(directory!, new Lumina.LuminaOptions { PanicOnSheetChecksumMismatch = false });
        var manager = Substitute.For<IDataManager>();
        manager.GetExcelSheet<Lumina.Excel.Sheets.Warp>().Returns(data.GetExcelSheet<Lumina.Excel.Sheets.Warp>());
        var resolver = new TravelDestinationResolver(manager, NullLogger<TravelDestinationResolver>.Instance);

        Assert.True(resolver.TryFindWarp(144, "Seek Passage to the Gold Saucer", out _, out _));
        Assert.True(resolver.TryFindWarp(144, "Travel to the Gold Saucer?", out _, out _));
        var airship = new QuestStep(EInteractionType.Interact, 1004433,
            new System.Numerics.Vector3(-23.605713f, 83.19999f, -2.3041382f), 130)
        { TargetTerritoryId = 144 };
        AssertMenu(airship, false, true, manager);
        AssertMenu(airship, true, true, manager);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NextQuest_AnswersItsTravelMenu(bool checkAllSteps)
    {
        AssertMenu(MenuStep(), checkAllSteps, true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableNext_IgnoresItsMenu(bool checkAllSteps)
    {
        AssertMenu(MenuStep(), checkAllSteps, false);
    }

    private static QuestStep MenuStep() => new()
    {
        DialogueChoices =
        [
            new()
            {
                Type = EDialogChoiceType.List,
                Answer = ExcelRef.FromSheetValue("Seek Passage to the Gold Saucer")
            }
        ]
    };

    private static void AssertMenu(QuestStep nextStep, bool checkAllSteps, bool nextReady, IDataManager? manager = null)
    {
        NoPluginsInstalled.Ensure();
        var state = new FakeQuestGameState();
        if (!nextReady)
            state.Complete.Add(435);
        var catalog = new FakeQuestCatalog();
        var started = catalog.Add(TestQuestInfo.Create(660), QuestTestData.Seq(0, new QuestStep()));
        var next = catalog.Add(TestQuestInfo.Create(435), QuestTestData.Seq(0, nextStep));
        var functions = new QuestFunctions(catalog, state, new AlliedSocietyData(), new Configuration(), Substitute.For<IChatGui>());
        var tracker = new QuestProgressTracker(functions, null!, null!, NullLogger<QuestProgressTracker>.Instance)
        {
            StartedQuest = new(started),
            NextQuest = new(next)
        };
        var controller = TestQuestController.Create(tracker: tracker);
        Assert.Same(nextReady ? tracker.NextQuest : tracker.StartedQuest, controller.CurrentQuest);

        var handler = (DialogueChoiceHandler)RuntimeHelpers.GetUninitializedObject(typeof(DialogueChoiceHandler));
        Set(handler, "_questController", controller);
        var targets = Substitute.For<ITargetManager>();
        targets.Target.Returns((Dalamud.Game.ClientState.Objects.Types.IGameObject?)null);
        Set(handler, "_targetManager", targets);
        Set(handler, "_clientState", Substitute.For<IClientState>());
        var territory = (TerritoryData)RuntimeHelpers.GetUninitializedObject(typeof(TerritoryData));
        Set(territory, "_dutyTerritories", System.Collections.Immutable.ImmutableDictionary<uint, uint>.Empty);
        Set(handler, "_territoryData", territory);
        Set(handler, "_travelDestinationResolver", new TravelDestinationResolver(manager!, NullLogger<TravelDestinationResolver>.Instance));
        Set(handler, "_dialogueReferenceResolver", new DialogueReferenceResolver(null!));
        Set(handler, "_configuration", new Configuration());
        Set(handler, "_logger", NullLogger<DialogueChoiceHandler>.Instance);
        List<string?> answers = ["Purchase Passage to Gridania", "Purchase Passage to Limsa Lominsa",
            "Seek Passage to the Gold Saucer", "Nothing"];

        object? result = typeof(DialogueChoiceHandler).GetMethod("HandleListChoice", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(handler, [null, answers, checkAllSteps]);

        Assert.Equal(nextReady ? 2 : (int?)null, result);
    }

    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}
