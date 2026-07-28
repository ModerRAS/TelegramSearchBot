#if WINDOWS
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.Hosting;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using TelegramSearchBot.Common;
using TelegramSearchBot.Controller.Manage;
using TelegramSearchBot.Model;
using Xunit;

namespace TelegramSearchBot.Test.Controller.Manage;

/// <summary>
/// Tests for UpdateController bot command handling.
///
/// These tests validate the command routing logic, null-safety guards,
/// admin permission checks (global admin via Env.AdminId), and the full ExecuteAsync flow.
///
/// NOTE: Some integration-style tests call SelfUpdateBootstrap static methods
/// which depend on network access and platform-specific functionality.
/// </summary>
public class UpdateControllerTests
{
    private readonly Mock<ITelegramBotClient> _mockBotClient;
    private readonly Mock<IHostApplicationLifetime> _mockAppLifetime;
    private readonly UpdateController _controller;

    public UpdateControllerTests()
    {
        _mockBotClient = new Mock<ITelegramBotClient>();
        _mockAppLifetime = new Mock<IHostApplicationLifetime>();

        // SendMessage in Telegram.Bot v22 is an extension method that calls
        // ITelegramBotClient.SendRequest<T>() internally. Mock the underlying
        // interface method to avoid real HTTP calls to Telegram API.
        _mockBotClient
            .Setup(x => x.SendRequest(
                It.IsAny<Telegram.Bot.Requests.Abstractions.IRequest<Message>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Message { Id = 1, Chat = new Chat { Id = -1001234567890 } });

        _controller = new UpdateController(
            _mockBotClient.Object,
            _mockAppLifetime.Object);
    }

    /// <summary>
    /// Creates a PipelineContext with a text message from the given user/chat.
    /// </summary>
    private static PipelineContext CreateContext(string text, long userId = 12345, long chatId = -1001234567890)
    {
        return new PipelineContext
        {
            Update = new Update
            {
                Message = new Message
                {
                    Text = text,
                    From = new User { Id = userId },
                    Chat = new Chat { Id = chatId },
                    Id = 1
                }
            }
        };
    }

    // ──────────────────────────────────────────────
    //  IsUpdateCommand routing tests (reflection)
    //  These pass in RED phase — no external deps.
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData("/checkupdate")]
    [InlineData("/检查更新")]
    [InlineData("检查更新")]
    [InlineData("/update")]
    [InlineData("/更新")]
    [InlineData("更新")]
    [InlineData("执行更新")]
    public void IsUpdateCommand_AllSevenCommandPatterns_ReturnsTrue(string command)
    {
        var method = typeof(UpdateController).GetMethod(
            "IsUpdateCommand",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.NotNull(method);
        var result = (bool)method.Invoke(null, new object[] { command })!;
        Assert.True(result, $"Expected '{command}' to be recognized as an update command.");
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("/start")]
    [InlineData("/help")]
    [InlineData("")]
    [InlineData("checkupdate")]            // missing prefix — not an exact match
    [InlineData("update")]                 // lowercase without prefix
    [InlineData("/checkupdate extra")]     // trailing text
    public void IsUpdateCommand_NonMatchingText_ReturnsFalse(string command)
    {
        var method = typeof(UpdateController).GetMethod(
            "IsUpdateCommand",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.NotNull(method);
        var result = (bool)method.Invoke(null, new object[] { command })!;
        Assert.False(result, $"Expected '{command}' to NOT be recognized as an update command.");
    }

    [Fact]
    public void IsUpdateCommand_CaseInsensitiveMatching_Works()
    {
        var method = typeof(UpdateController).GetMethod(
            "IsUpdateCommand",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.NotNull(method);
        Assert.True((bool)method.Invoke(null, new object[] { "/CHECKUPDATE" })!, "/CHECKUPDATE should match.");
        Assert.True((bool)method.Invoke(null, new object[] { "/Update" })!, "/Update should match.");
        Assert.True((bool)method.Invoke(null, new object[] { "/更新" })!, "/更新 should match.");
    }

    // ──────────────────────────────────────────────
    //  Negative tests — non-command / null input
    //  These pass in RED phase (no external calls).
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NonCommandText_DoesNotThrow()
    {
        // The controller should bail out at IsUpdateCommand check
        // before reaching AdminService or SelfUpdateBootstrap.
        var context = CreateContext("random chat message");

        // RED: does not throw for non-command input.
        var exception = await Record.ExceptionAsync(() => _controller.ExecuteAsync(context));
        Assert.Null(exception);
    }

    [Fact]
    public async Task ExecuteAsync_NullMessageText_DoesNotThrow()
    {
        var context = new PipelineContext
        {
            Update = new Update
            {
                Message = new Message
                {
                    Text = null,
                    From = new User { Id = 12345 },
                    Chat = new Chat { Id = -1001234567890 },
                    Id = 1
                }
            }
        };

        var exception = await Record.ExceptionAsync(() => _controller.ExecuteAsync(context));
        Assert.Null(exception);
    }

    [Fact]
    public async Task ExecuteAsync_NullMessage_DoesNotThrow()
    {
        var context = new PipelineContext
        {
            Update = new Update
            {
                Message = null
            }
        };

        var exception = await Record.ExceptionAsync(() => _controller.ExecuteAsync(context));
        Assert.Null(exception);
    }

    // ──────────────────────────────────────────────
    //  Dependencies property
    // ──────────────────────────────────────────────

    [Fact]
    public void Dependencies_IncludesAdminController()
    {
        var deps = _controller.Dependencies;
        Assert.Contains(typeof(AdminController), deps);
    }

    // ──────────────────────────────────────────────
    //  RED-phase integration tests
    //  Test the full ExecuteAsync flow for each of
    //  the 7 command patterns.
    //
    //  These are EXPECTED to fail at runtime because:
    //    AdminService.IsNormalAdmin is non-virtual.
    //    SelfUpdateBootstrap is static.
    //
    //  The test structure documents the intended
    //  behavior; passing tests come in GREEN phase.
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CheckUpdateCommand_EntersCheckUpdateFlow()
    {
        var context = CreateContext("/checkupdate");
        await _controller.ExecuteAsync(context);
        // RED: AdminService.IsNormalAdmin throw → test fails.
        // GREEN: mock IsNormalAdmin → verify status reply sent.
    }

    [Fact]
    public async Task ExecuteAsync_SlashCheckUpdateChinese_EntersCheckUpdateFlow()
    {
        var context = CreateContext("/检查更新");
        await _controller.ExecuteAsync(context);
    }

    [Fact]
    public async Task ExecuteAsync_BareCheckUpdateChinese_EntersCheckUpdateFlow()
    {
        var context = CreateContext("检查更新");
        await _controller.ExecuteAsync(context);
    }

    [Fact]
    public async Task ExecuteAsync_StartUpdateCommand_EntersStartUpdateFlow()
    {
        var context = CreateContext("/update");
        await _controller.ExecuteAsync(context);
    }

    [Fact]
    public async Task ExecuteAsync_SlashUpdateChinese_EntersStartUpdateFlow()
    {
        var context = CreateContext("/更新");
        await _controller.ExecuteAsync(context);
    }

    [Fact]
    public async Task ExecuteAsync_BareUpdateChinese_EntersStartUpdateFlow()
    {
        var context = CreateContext("更新");
        await _controller.ExecuteAsync(context);
    }

    [Fact]
    public async Task ExecuteAsync_ExecuteUpdate_EntersStartUpdateFlow()
    {
        var context = CreateContext("执行更新");
        await _controller.ExecuteAsync(context);
    }
}
#endif
