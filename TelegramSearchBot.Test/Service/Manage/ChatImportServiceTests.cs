using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot;
using TelegramSearchBot.Model;
using TelegramSearchBot.Service.Manage;
using Xunit;

namespace TelegramSearchBot.Test.Service.Manage {
    public class ChatImportServiceTests {
        private readonly DbContextOptions<DataDbContext> _dbContextOptions;

        public ChatImportServiceTests() {
            _dbContextOptions = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        }

        [Fact]
        public async Task ImportFromFileAsync_TextOnlyExport_DoesNotRequireEmbeddedMediaFiles() {
            var tempDir = Path.Combine(Path.GetTempPath(), $"chat-import-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try {
                var jsonPath = Path.Combine(tempDir, "result.json");
                await File.WriteAllTextAsync(jsonPath, """
                {
                  "name": "测试频道",
                  "type": "channel",
                  "id": 777,
                  "messages": [
                    {
                      "id": 1,
                      "type": "service",
                      "date": "2025-08-09T07:33:01",
                      "date_unixtime": "1754695981",
                      "actor": "夸克网盘",
                      "actor_id": "channel2726412745",
                      "action": "create_channel",
                      "title": "夸克网盘",
                      "text": "",
                      "text_entities": []
                    },
                    {
                      "id": 2,
                      "type": "message",
                      "date": "2025-08-09T07:57:05",
                      "date_unixtime": "1754697425",
                      "from": "夸克网盘",
                      "from_id": "channel2726412745",
                      "photo": "(File not included. Change data exporting settings to download.)",
                      "photo_file_size": 257958,
                      "width": 905,
                      "height": 1280,
                      "text": [
                        "链接：",
                        {
                          "type": "link",
                          "text": "https://pan.quark.cn/s/fa9a2f5f8757"
                        }
                      ],
                      "text_entities": [
                        {
                          "type": "plain",
                          "text": "链接："
                        },
                        {
                          "type": "link",
                          "text": "https://pan.quark.cn/s/fa9a2f5f8757"
                        }
                      ]
                    },
                    {
                      "id": 3,
                      "type": "message",
                      "date": "2025-08-09T08:00:00",
                      "date_unixtime": "1754697600",
                      "from": "Alice",
                      "from_id": "user123456",
                      "text": "plain string text",
                      "text_entities": []
                    },
                    {
                      "id": 4,
                      "type": "message",
                      "date": "2025-08-09T08:01:00",
                      "date_unixtime": "1754697660",
                      "from": "Bob",
                      "from_id": "user654321",
                      "photo": "(File not included. Change data exporting settings to download.)",
                      "text": "",
                      "text_entities": [],
                      "caption_entities": [
                        {
                          "type": "bold",
                          "text": "加粗标题"
                        }
                      ]
                    }
                  ]
                }
                """);

                await using var context = new DataDbContext(_dbContextOptions);
                var service = new ChatImportService(
                    new Mock<ILogger<ChatImportService>>().Object,
                    new TestSendMessage(),
                    context);

                await service.ImportFromFileAsync(jsonPath);

                var imported = await context.Messages
                    .Where(m => m.GroupId == 777)
                    .OrderBy(m => m.MessageId)
                    .ToListAsync();

                Assert.Equal(4, imported.Count);
                Assert.Equal(2726412745L, imported[0].FromUserId);
                Assert.Contains("create_channel", imported[0].Content);
                Assert.Equal(2726412745L, imported[1].FromUserId);
                Assert.Contains("https://pan.quark.cn/s/fa9a2f5f8757", imported[1].Content);
                Assert.Equal("plain string text", imported[2].Content);
                Assert.Equal(123456L, imported[2].FromUserId);
                Assert.Contains("**加粗标题**", imported[3].Content);
                Assert.Equal(654321L, imported[3].FromUserId);
            } finally {
                Directory.Delete(tempDir, true);
            }
        }

        private sealed class TestSendMessage : TelegramSearchBot.Manager.SendMessage {
            public TestSendMessage()
                : base(
                    new Mock<ITelegramBotClient>().Object,
                    new Mock<ILogger<TelegramSearchBot.Manager.SendMessage>>().Object) {
            }

            public override Task AddTask(Func<Task> action, bool isGroup) {
                return Task.CompletedTask;
            }
        }
    }
}
