#if WINDOWS
using System.Reflection;
using Microsoft.Extensions.Hosting;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Controller.Manage;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.Common;
using TelegramSearchBot.Service.Manage;
using TelegramSearchBot.Service.Scheduler;
using Xunit;

namespace TelegramSearchBot.Test.Controller.Manage;

/// <summary>
/// RED-phase tests for UpdateController bot command handling.
///
/// These tests validate the command routing logic, null-safety guards,
/// admin permission checks, and the full ExecuteAsync flow.
///
/// NOTE: Many integration-style tests are EXPECTED to fail at runtime
/// in the RED phase because:
///   1. AdminService.IsNormalAdmin is non-virtual and cannot be mocked.
///   2. SelfUpdateBootstrap is a static class with platform-dependent partial methods.
///   3. Telegram.Bot v22 API methods are extension methods (not interface methods)
///      and cannot be mocked by Moq.
///
/// The test structure is correct and compiles; the GREEN phase (Task 10)
/// will make the system-under-test fully mockable.
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

        // AdminService constructor requires several dependencies.
        // We mock all constructor parameters to enable object creation.
        // IsNormalAdmin is non-virtual — the mock proxy will throw
        // at runtime when called; this is expected RED behavior.
        var mockAdminService = new Mock<AdminService>(
            Mock.Of<Microsoft.Extensions.Logging.ILogger<AdminService>>(),
            null!, // DataDbContext — null is acceptable for RED phase
            Mock.Of<IAppConfigurationService>(),
            Mock.Of<StackExchange.Redis.IConnectionMultiplexer>(),
            Mock.Of<ISchedulerService>());

        _controller = new UpdateController(
            _mockBotClient.Object,
            mockAdminService.Object,
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
