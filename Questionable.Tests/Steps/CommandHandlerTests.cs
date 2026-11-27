using Questionable.Controller;
using Xunit;

namespace Questionable.Tests.Steps;

public sealed class CommandHandlerTests
{
    [Theory]
    [InlineData("do")]
    [InlineData("sim")]
    [InlineData("mountid")]
    [InlineData("gen")]
    [InlineData("d2qwh")]
    [InlineData("d2qwl")]
    [InlineData("rewards")]
    [InlineData("tname")]
    [InlineData("eventquests")]
    [InlineData("unlock-links")]
    [InlineData("taxi")]
    [InlineData("navcheck")]
    [InlineData("festivals")]
    [InlineData("loc")]
    public void PathTools_NeedDebug(string subcommand) => Assert.True(CommandHandler.NeedsDebug(subcommand));

    [Theory]
    [InlineData("")]
    [InlineData("help")]
    [InlineData("help-all")]
    [InlineData("config")]
    [InlineData("start")]
    [InlineData("dailies")]
    [InlineData("stop")]
    [InlineData("reload")]
    [InlineData("skipstep")]
    [InlineData("next")]
    [InlineData("which")]
    [InlineData("zone")]
    [InlineData("journal")]
    [InlineData("priority")]
    [InlineData("cleanup")]
    [InlineData("handle-interrupt")]
    [InlineData("redeem")]
    [InlineData("mail")]
    [InlineData("validation")]
    [InlineData("whatsnew")]
    [InlineData("debug")]
    [InlineData("debugmode")]
    public void PlayerCommands_StayOpen(string subcommand) => Assert.False(CommandHandler.NeedsDebug(subcommand));
}
