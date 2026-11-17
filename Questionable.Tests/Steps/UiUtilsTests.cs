// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Kagekazu
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using NSubstitute;
using Questionable.Data;
using Questionable.Domain;
using Questionable.Functions;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Questionable.Windows;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class UiUtilsTests
{
    private readonly FakeQuestGameState _state = new();
    private readonly FakeQuestCatalog _catalog = new();
    private readonly UiUtils _ui;
    private readonly QuestId _daily = new(1222);

    public UiUtilsTests()
    {
        _catalog.Add(TestQuestInfo.Create(1222, level: 50)
            .With(nameof(QuestInfo.AlliedSociety), EAlliedSociety.Amaljaa)
            .With(nameof(QuestInfo.IsRepeatable), true));
        _state.AvailableAlliedSocietyQuests[EAlliedSociety.Amaljaa] = [_daily];
        var functions = new QuestFunctions(_catalog, _state, new AlliedSocietyData(),
            new Configuration(), Substitute.For<IChatGui>());
        _ui = new UiUtils(functions, null!, _catalog, null!);
    }

    [Fact]
    public void NoAllowances_IsNotComplete()
    {
        _state.SocietyAllowances = 0;
        var style = _ui.GetQuestStyle(_daily);
        Assert.Equal(FontAwesomeIcon.Lock, style.Icon);
        Assert.Contains("allowances", style.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LowLevel_IsNotComplete()
    {
        _state.CurrentLevel = 49;
        var style = _ui.GetQuestStyle(_daily);
        Assert.Equal(FontAwesomeIcon.Lock, style.Icon);
        Assert.Contains("level", style.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LockedDaily_IsNotComplete()
    {
        _catalog.Add(TestQuestInfo.Create(1222, previous: [new(new QuestId(1))])
            .With(nameof(QuestInfo.AlliedSociety), EAlliedSociety.Amaljaa)
            .With(nameof(QuestInfo.IsRepeatable), true));
        _catalog.Add(TestQuestInfo.Create(1, "Prerequisite"));
        var style = _ui.GetQuestStyle(_daily);
        Assert.Equal(FontAwesomeIcon.Lock, style.Icon);
        Assert.Contains("Prerequisite", style.Status);
    }

    [Fact]
    public void OldCompletion_IsNotTodays()
    {
        _state.Complete.Add(_daily.Value);
        _state.AvailableAlliedSocietyQuests.Clear();
        var style = _ui.GetQuestStyle(_daily);
        Assert.Equal(FontAwesomeIcon.Lock, style.Icon);
        Assert.Contains("unavailable", style.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TodayComplete_UsesTodaysFlag(bool offered)
    {
        _state.DailyCompleted.Add(_daily.Value);
        _state.SocietyAllowances = 0;
        _state.CurrentLevel = 1;
        if (!offered)
            _state.AvailableAlliedSocietyQuests.Clear();
        var style = _ui.GetQuestStyle(_daily);
        Assert.Equal(FontAwesomeIcon.Check, style.Icon);
        Assert.Equal("Complete", style.Status);
    }

    [Fact]
    public void AcceptedDaily_StaysActive()
    {
        _state.Accepted.Add(_daily.Value);
        _state.DailyCompleted.Add(_daily.Value);
        _state.SocietyAllowances = 0;
        _state.AvailableAlliedSocietyQuests.Clear();
        var style = _ui.GetQuestStyle(_daily);
        Assert.Equal(FontAwesomeIcon.PersonWalkingArrowRight, style.Icon);
        Assert.Equal("Active", style.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OfferedDaily_IsAvailable(bool previouslyDone)
    {
        if (previouslyDone)
            _state.Complete.Add(_daily.Value);
        var style = _ui.GetQuestStyle(_daily);
        Assert.Equal(FontAwesomeIcon.Running, style.Icon);
        Assert.Equal("Available", style.Status);
    }

    [Fact]
    public void StoryCompletion_IsUnchanged()
    {
        _catalog.Add(TestQuestInfo.Create(1));
        _state.Complete.Add(1);
        var style = _ui.GetQuestStyle(new QuestId(1));
        Assert.Equal(FontAwesomeIcon.Check, style.Icon);
        Assert.Equal("Complete", style.Status);
    }
}
