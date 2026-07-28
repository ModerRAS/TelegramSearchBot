using System.Reflection;
using TelegramSearchBot.Controller.Manage;
using Xunit;

namespace TelegramSearchBot.Test.Controller.Manage;

public class LlmVisibilityControllerTests
{
    [Theory]
    [InlineData("/llm_invisible_on")]
    [InlineData("/llm_invisible_on@TelegramSearchBot")]
    [InlineData("/LLM_INVISIBLE_ON@TelegramSearchBot")]
    [InlineData("LLM隐身")]
    public void IsEnableCommand_MatchesSupportedCommands(string command)
    {
        Assert.True(InvokeBool("IsEnableCommand", command));
    }

    [Theory]
    [InlineData("/llm_invisible_off")]
    [InlineData("/llm_invisible_off@TelegramSearchBot")]
    [InlineData("取消LLM隐身")]
    [InlineData("LLM显身")]
    public void IsDisableCommand_MatchesSupportedCommands(string command)
    {
        Assert.True(InvokeBool("IsDisableCommand", command));
    }

    [Theory]
    [InlineData("/llm_invisible_status")]
    [InlineData("/llm_invisible_status@TelegramSearchBot")]
    [InlineData("LLM隐身状态")]
    public void IsStatusCommand_MatchesSupportedCommands(string command)
    {
        Assert.True(InvokeBool("IsStatusCommand", command));
    }

    [Theory]
    [InlineData("/llm_invisible_on_bad")]
    [InlineData("/llm_invisible_off_bad")]
    [InlineData("/llm_invisible_status_bad")]
    public void SlashCommandMatching_DoesNotMatchLongerNames(string command)
    {
        Assert.False(InvokeBool("IsEnableCommand", command));
        Assert.False(InvokeBool("IsDisableCommand", command));
        Assert.False(InvokeBool("IsStatusCommand", command));
    }

    [Fact]
    public void NormalizeCommand_PreservesBotSuffixAndDropsArguments()
    {
        var method = typeof(LlmVisibilityController).GetMethod(
            "NormalizeCommand",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (string)method.Invoke(null, new object[] { "/llm_invisible_on@TelegramSearchBot extra" })!;

        Assert.Equal("/llm_invisible_on@TelegramSearchBot", result);
    }

    private static bool InvokeBool(string methodName, string command)
    {
        var method = typeof(LlmVisibilityController).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static)!;

        return (bool)method.Invoke(null, new object[] { command })!;
    }
}
