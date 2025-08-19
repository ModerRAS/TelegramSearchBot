using System;
using System.Threading.Tasks;
using Moq;
using Telegram.Bot.Types;
using TelegramSearchBot.Controller.Bilibili;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Model;
using TelegramSearchBot.Test.Controllers;
using Xunit;

namespace TelegramSearchBot.Test.Controllers.Bilibili
{
    /// <summary>
    /// BiliMessageController测试
    /// 
    /// 测试B站链接处理功能
    /// </summary>
    public class BiliMessageControllerTests : ControllerTestBase
    {
        private readonly BiliMessageController _controller;

        public BiliMessageControllerTests()
        {
            _controller = new BiliMessageController(
                BotClientMock.Object,
                SendMessageServiceMock.Object,
                MessageServiceMock.Object,
                LoggerMock.Object,
                MessageExtensionServiceMock.Object
            );
        }

        [Theory]
        [InlineData("https://www.bilibili.com/video/BV1xx411c7mD")]
        [InlineData("https://b23.tv/BV1xx411c7mD")]
        [InlineData("https://m.bilibili.com/video/BV1xx411c7mD")]
        public async Task ExecuteAsync_WithBilibiliVideoLink_ShouldProcess(string videoUrl)
        {
            // Arrange
            var update = CreateTestUpdate(text: $"看看这个视频：{videoUrl}");
            var context = CreatePipelineContext(update);
            
            SetupMessageService(1001);
            
            // Act
            await _controller.ExecuteAsync(context);
            
            // Assert
            MessageServiceMock.Verify(
                x => x.ExecuteAsync(It.Is<MessageOption>(opt => 
                    opt.Content.Contains(videoUrl))),
                Times.Once);
        }

        [Theory]
        [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
        [InlineData("https://github.com/user/repo")]
        [InlineData("Just a regular message")]
        public async Task ExecuteAsync_WithNonBilibiliLinks_ShouldIgnore(string text)
        {
            // Arrange
            var update = CreateTestUpdate(text: text);
            var context = CreatePipelineContext(update);
            
            // Act
            await _controller.ExecuteAsync(context);
            
            // Assert
            MessageServiceMock.Verify(
                x => x.ExecuteAsync(It.IsAny<MessageOption>()),
                Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_WithBilibiliLinkInReply_ShouldProcess()
        {
            // Arrange
            var update = CreateReplyUpdate(
                text: "https://www.bilibili.com/video/BV1xx411c7mD",
                replyToMessageId: 54321
            );
            
            var context = CreatePipelineContext(update);
            SetupMessageService(1002);
            
            // Act
            await _controller.ExecuteAsync(context);
            
            // Assert
            MessageServiceMock.Verify(
                x => x.ExecuteAsync(It.Is<MessageOption>(opt => 
                    opt.ReplyTo == 54321)),
                Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_WithMultipleBilibiliLinks_ShouldProcessAll()
        {
            // Arrange
            var update = CreateTestUpdate(
                text: @"分享几个视频：
                https://www.bilibili.com/video/BV1xx411c7mD
                https://www.bilibili.com/video/BV1xx411c7m2"
            );
            
            var context = CreatePipelineContext(update);
            SetupMessageService(1003);
            
            // Act
            await _controller.ExecuteAsync(context);
            
            // Assert
            MessageServiceMock.Verify(
                x => x.ExecuteAsync(It.Is<MessageOption>(opt => 
                    opt.Content.Contains("BV1xx411c7mD") && 
                    opt.Content.Contains("BV1xx411c7m2"))),
                Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_WithBilibiliLinkAndOtherText_ShouldExtract()
        {
            // Arrange
            var update = CreateTestUpdate(
                text: "我刚看完这个视频，感觉不错：https://www.bilibili.com/video/BV1xx411c7mD 大家可以看看"
            );
            
            var context = CreatePipelineContext(update);
            SetupMessageService(1004);
            
            // Act
            await _controller.ExecuteAsync(context);
            
            // Assert
            MessageServiceMock.Verify(
                x => x.ExecuteAsync(It.Is<MessageOption>(opt => 
                    opt.Content.Contains("BV1xx411c7mD"))),
                Times.Once);
        }

        [Fact]
        public void Dependencies_ShouldBeEmpty()
        {
            // Act
            var dependencies = _controller.Dependencies;
            
            // Assert
            Assert.NotNull(dependencies);
            Assert.Empty(dependencies);
        }

        [Fact]
        public void Constructor_WithNullDependencies_ShouldThrow()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new BiliMessageController(null, SendMessageServiceMock.Object, 
                    MessageServiceMock.Object, LoggerMock.Object, MessageExtensionServiceMock.Object));
            
            Assert.Throws<ArgumentNullException>(() => 
                new BiliMessageController(BotClientMock.Object, null, 
                    MessageServiceMock.Object, LoggerMock.Object, MessageExtensionServiceMock.Object));
        }

        [Fact]
        public async Task ExecuteAsync_WithMessageServiceError_ShouldHandleGracefully()
        {
            // Arrange
            var update = CreateTestUpdate(text: "https://www.bilibili.com/video/BV1xx411c7mD");
            var context = CreatePipelineContext(update);
            
            MessageServiceMock
                .Setup(x => x.ExecuteAsync(It.IsAny<MessageOption>()))
                .ThrowsAsync(new Exception("Service unavailable"));
            
            // Act & Assert
            await Assert.ThrowsAsync<Exception>(() => _controller.ExecuteAsync(context));
        }

        [Fact]
        public async Task ExecuteAsync_WithShortBilibiliLink_ShouldProcess()
        {
            // Arrange
            var update = CreateTestUpdate(text: "b23.tv/BV1xx411c7mD");
            var context = CreatePipelineContext(update);
            SetupMessageService(1005);
            
            // Act
            await _controller.ExecuteAsync(context);
            
            // Assert
            MessageServiceMock.Verify(
                x => x.ExecuteAsync(It.Is<MessageOption>(opt => 
                    opt.Content.Contains("b23.tv/BV1xx411c7mD"))),
                Times.Once);
        }
    }
}