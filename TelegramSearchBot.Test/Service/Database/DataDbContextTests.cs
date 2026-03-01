using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using Xunit;

namespace TelegramSearchBot.Test.Service.Database {
    /// <summary>
    /// Tests for DataDbContext: creation, entity configuration, indexes, and relationships
    /// </summary>
    public class DataDbContextTests {
        private DbContextOptions<DataDbContext> CreateOptions() {
            return new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
        }

        [Fact]
        public void DataDbContext_CanBeCreated() {
            var options = CreateOptions();
            using var context = new DataDbContext(options);
            Assert.NotNull(context);
        }

        [Fact]
        public async Task DataDbContext_CanSaveAndRetrieveMessage() {
            var options = CreateOptions();
            using (var context = new DataDbContext(options)) {
                context.Messages.Add(new Message {
                    GroupId = 100,
                    MessageId = 1000,
                    FromUserId = 1,
                    Content = "Hello World",
                    DateTime = DateTime.UtcNow
                });
                await context.SaveChangesAsync();
            }

            using (var context = new DataDbContext(options)) {
                var msg = await context.Messages.FirstAsync();
                Assert.Equal(100, msg.GroupId);
                Assert.Equal(1000, msg.MessageId);
                Assert.Equal("Hello World", msg.Content);
            }
        }

        [Fact]
        public async Task DataDbContext_CanSaveAndRetrieveUserData() {
            var options = CreateOptions();
            using (var context = new DataDbContext(options)) {
                context.UserData.Add(new UserData {
                    Id = 42,
                    FirstName = "John",
                    LastName = "Doe",
                    UserName = "johndoe",
                    IsPremium = true,
                    IsBot = false
                });
                await context.SaveChangesAsync();
            }

            using (var context = new DataDbContext(options)) {
                var user = await context.UserData.FindAsync(42L);
                Assert.NotNull(user);
                Assert.Equal("John", user.FirstName);
                Assert.Equal("Doe", user.LastName);
                Assert.True(user.IsPremium);
            }
        }

        [Fact]
        public async Task DataDbContext_CanSaveAndRetrieveGroupData() {
            var options = CreateOptions();
            using (var context = new DataDbContext(options)) {
                context.GroupData.Add(new GroupData {
                    Id = 100,
                    Type = "supergroup",
                    Title = "Test Group",
                    IsForum = false,
                    IsBlacklist = false
                });
                await context.SaveChangesAsync();
            }

            using (var context = new DataDbContext(options)) {
                var group = await context.GroupData.FindAsync(100L);
                Assert.NotNull(group);
                Assert.Equal("Test Group", group.Title);
            }
        }

        [Fact]
        public async Task DataDbContext_AccountBookRecordRelationship() {
            var options = CreateOptions();
            using (var context = new DataDbContext(options)) {
                var book = new AccountBook {
                    GroupId = 100,
                    Name = "Test Book",
                    Description = "Test",
                    CreatedBy = 1
                };
                context.AccountBooks.Add(book);
                await context.SaveChangesAsync();

                context.AccountRecords.Add(new AccountRecord {
                    AccountBookId = book.Id,
                    Amount = 100.50m,
                    Tag = "food",
                    Description = "Lunch",
                    CreatedBy = 1,
                    CreatedByUsername = "testuser"
                });
                await context.SaveChangesAsync();
            }

            using (var context = new DataDbContext(options)) {
                var book = await context.AccountBooks
                    .Include(b => b.Records)
                    .FirstAsync();
                Assert.Single(book.Records);
                Assert.Equal(100.50m, book.Records.First().Amount);
                Assert.Equal("food", book.Records.First().Tag);
            }
        }

        [Fact]
        public async Task DataDbContext_ConversationSegmentMessageRelationship() {
            var options = CreateOptions();
            using (var context = new DataDbContext(options)) {
                var message = new Message {
                    GroupId = 100,
                    MessageId = 1,
                    FromUserId = 1,
                    Content = "Hello",
                    DateTime = DateTime.UtcNow
                };
                context.Messages.Add(message);
                await context.SaveChangesAsync();

                var segment = new ConversationSegment {
                    GroupId = 100,
                    StartTime = DateTime.UtcNow.AddMinutes(-10),
                    EndTime = DateTime.UtcNow,
                    FirstMessageId = 1,
                    LastMessageId = 1,
                    MessageCount = 1,
                    ParticipantCount = 1,
                    ContentSummary = "Test conversation",
                    FullContent = "Hello"
                };
                context.ConversationSegments.Add(segment);
                await context.SaveChangesAsync();

                context.ConversationSegmentMessages.Add(new ConversationSegmentMessage {
                    ConversationSegmentId = segment.Id,
                    MessageDataId = message.Id,
                    SequenceOrder = 0
                });
                await context.SaveChangesAsync();
            }

            using (var context = new DataDbContext(options)) {
                var segment = await context.ConversationSegments
                    .Include(s => s.Messages)
                    .FirstAsync();
                Assert.Single(segment.Messages);
            }
        }

        [Fact]
        public async Task DataDbContext_LLMChannelWithModelRelationship() {
            var options = CreateOptions();
            using (var context = new DataDbContext(options)) {
                var channel = new LLMChannel {
                    Name = "Test Channel",
                    Gateway = "http://localhost:11434",
                    ApiKey = "test-key",
                    Provider = TelegramSearchBot.Model.AI.LLMProvider.Ollama,
                    Parallel = 2,
                    Priority = 1
                };
                context.LLMChannels.Add(channel);
                await context.SaveChangesAsync();

                context.ChannelsWithModel.Add(new ChannelWithModel {
                    ModelName = "llama3",
                    LLMChannelId = channel.Id
                });
                await context.SaveChangesAsync();
            }

            using (var context = new DataDbContext(options)) {
                var channel = await context.LLMChannels.FirstAsync();
                Assert.Equal("Test Channel", channel.Name);

                var model = await context.ChannelsWithModel.FirstAsync();
                Assert.Equal("llama3", model.ModelName);
                Assert.Equal(channel.Id, model.LLMChannelId);
            }
        }

        [Fact]
        public async Task DataDbContext_AppConfigurationItemCRUD() {
            var options = CreateOptions();
            using (var context = new DataDbContext(options)) {
                context.AppConfigurationItems.Add(new AppConfigurationItem {
                    Key = "TestKey",
                    Value = "TestValue"
                });
                await context.SaveChangesAsync();
            }

            // Read
            using (var context = new DataDbContext(options)) {
                var item = await context.AppConfigurationItems.FindAsync("TestKey");
                Assert.NotNull(item);
                Assert.Equal("TestValue", item.Value);
            }

            // Update
            using (var context = new DataDbContext(options)) {
                var item = await context.AppConfigurationItems.FindAsync("TestKey");
                item.Value = "UpdatedValue";
                await context.SaveChangesAsync();
            }

            using (var context = new DataDbContext(options)) {
                var item = await context.AppConfigurationItems.FindAsync("TestKey");
                Assert.Equal("UpdatedValue", item.Value);
            }

            // Delete
            using (var context = new DataDbContext(options)) {
                var item = await context.AppConfigurationItems.FindAsync("TestKey");
                context.AppConfigurationItems.Remove(item);
                await context.SaveChangesAsync();
            }

            using (var context = new DataDbContext(options)) {
                Assert.Empty(context.AppConfigurationItems);
            }
        }

        [Fact]
        public async Task DataDbContext_MessageExtensionRelationship() {
            var options = CreateOptions();
            using (var context = new DataDbContext(options)) {
                var message = new Message {
                    GroupId = 100,
                    MessageId = 1,
                    FromUserId = 1,
                    Content = "Test",
                    DateTime = DateTime.UtcNow
                };
                context.Messages.Add(message);
                await context.SaveChangesAsync();

                context.MessageExtensions.Add(new MessageExtension {
                    MessageDataId = message.Id,
                    Name = "ocr_text",
                    Value = "Extracted text from image"
                });
                await context.SaveChangesAsync();
            }

            using (var context = new DataDbContext(options)) {
                var message = await context.Messages
                    .Include(m => m.MessageExtensions)
                    .FirstAsync();
                Assert.Single(message.MessageExtensions);
                Assert.Equal("ocr_text", message.MessageExtensions.First().Name);
            }
        }

        [Fact]
        public async Task DataDbContext_VectorIndexCRUD() {
            var options = CreateOptions();
            using (var context = new DataDbContext(options)) {
                context.VectorIndexes.Add(new VectorIndex {
                    GroupId = 100,
                    VectorType = "Message",
                    EntityId = 1,
                    FaissIndex = 0,
                    ContentSummary = "Test vector"
                });
                await context.SaveChangesAsync();
            }

            using (var context = new DataDbContext(options)) {
                var vi = await context.VectorIndexes.FirstAsync();
                Assert.Equal("Message", vi.VectorType);
                Assert.Equal(0, vi.FaissIndex);
            }
        }

        [Fact]
        public async Task DataDbContext_ScheduledTaskExecutionCRUD() {
            var options = CreateOptions();
            using (var context = new DataDbContext(options)) {
                context.ScheduledTaskExecutions.Add(new ScheduledTaskExecution {
                    TaskName = "TestTask",
                    Status = TaskExecutionStatus.Running,
                    StartTime = DateTime.UtcNow
                });
                await context.SaveChangesAsync();
            }

            using (var context = new DataDbContext(options)) {
                var task = await context.ScheduledTaskExecutions.FirstAsync();
                Assert.Equal("TestTask", task.TaskName);
                Assert.Equal(TaskExecutionStatus.Running, task.Status);
            }
        }

        [Fact]
        public async Task DataDbContext_MemoryGraphCRUD() {
            var options = CreateOptions();
            using (var context = new DataDbContext(options)) {
                context.MemoryGraphs.Add(new MemoryGraph {
                    ChatId = 100,
                    Name = "TestEntity",
                    EntityType = "Person",
                    Observations = "Test observation",
                    ItemType = "entity"
                });
                await context.SaveChangesAsync();
            }

            using (var context = new DataDbContext(options)) {
                var entity = await context.MemoryGraphs.FirstAsync();
                Assert.Equal("TestEntity", entity.Name);
                Assert.Equal("Person", entity.EntityType);
            }
        }

        [Fact]
        public async Task DataDbContext_ShortUrlMappingCRUD() {
            var options = CreateOptions();
            using (var context = new DataDbContext(options)) {
                context.ShortUrlMappings.Add(new ShortUrlMapping {
                    OriginalUrl = "https://t.co/abc123",
                    ExpandedUrl = "https://example.com/full-article"
                });
                await context.SaveChangesAsync();
            }

            using (var context = new DataDbContext(options)) {
                var mapping = await context.ShortUrlMappings.FirstAsync();
                Assert.Equal("https://t.co/abc123", mapping.OriginalUrl);
                Assert.Equal("https://example.com/full-article", mapping.ExpandedUrl);
            }
        }

        [Fact]
        public async Task DataDbContext_TelegramFileCacheEntryCRUD() {
            var options = CreateOptions();
            using (var context = new DataDbContext(options)) {
                context.TelegramFileCacheEntries.Add(new TelegramFileCacheEntry {
                    CacheKey = "test-cache-key",
                    FileId = "file-id-123",
                    ExpiryDate = DateTime.UtcNow.AddHours(1)
                });
                await context.SaveChangesAsync();
            }

            using (var context = new DataDbContext(options)) {
                var entry = await context.TelegramFileCacheEntries.FindAsync("test-cache-key");
                Assert.NotNull(entry);
                Assert.Equal("file-id-123", entry.FileId);
            }
        }

        [Fact]
        public async Task DataDbContext_GroupSettingsCRUD() {
            var options = CreateOptions();
            using (var context = new DataDbContext(options)) {
                context.GroupSettings.Add(new GroupSettings {
                    GroupId = 100,
                    LLMModelName = "gpt-4",
                    IsManagerGroup = true
                });
                await context.SaveChangesAsync();
            }

            using (var context = new DataDbContext(options)) {
                var settings = await context.GroupSettings.FirstAsync();
                Assert.Equal(100, settings.GroupId);
                Assert.Equal("gpt-4", settings.LLMModelName);
                Assert.True(settings.IsManagerGroup);
            }
        }

        [Fact]
        public async Task DataDbContext_GroupAccountSettingsCRUD() {
            var options = CreateOptions();
            using (var context = new DataDbContext(options)) {
                context.GroupAccountSettings.Add(new GroupAccountSettings {
                    GroupId = 100,
                    IsAccountingEnabled = true,
                    ActiveAccountBookId = 1
                });
                await context.SaveChangesAsync();
            }

            using (var context = new DataDbContext(options)) {
                var settings = await context.GroupAccountSettings.FirstAsync();
                Assert.Equal(100, settings.GroupId);
                Assert.True(settings.IsAccountingEnabled);
            }
        }

        [Fact]
        public async Task DataDbContext_SearchPageCacheCRUD() {
            var options = CreateOptions();
            var uuid = Guid.NewGuid().ToString();
            using (var context = new DataDbContext(options)) {
                context.SearchPageCaches.Add(new SearchPageCache {
                    UUID = uuid,
                    SearchOptionJson = "{\"Search\":\"test\",\"ChatId\":100}"
                });
                await context.SaveChangesAsync();
            }

            using (var context = new DataDbContext(options)) {
                var cache = await context.SearchPageCaches.FirstOrDefaultAsync(c => c.UUID == uuid);
                Assert.NotNull(cache);
                Assert.Contains("test", cache.SearchOptionJson);
            }
        }

        [Fact]
        public async Task DataDbContext_UserWithGroupCRUD() {
            var options = CreateOptions();
            using (var context = new DataDbContext(options)) {
                context.UsersWithGroup.Add(new UserWithGroup {
                    GroupId = 100,
                    UserId = 42
                });
                await context.SaveChangesAsync();
            }

            using (var context = new DataDbContext(options)) {
                var uwg = await context.UsersWithGroup.FirstAsync();
                Assert.Equal(100, uwg.GroupId);
                Assert.Equal(42, uwg.UserId);
            }
        }

        [Fact]
        public async Task DataDbContext_FaissIndexFileCRUD() {
            var options = CreateOptions();
            using (var context = new DataDbContext(options)) {
                context.FaissIndexFiles.Add(new FaissIndexFile {
                    GroupId = 100,
                    IndexType = "Message",
                    FilePath = "/data/faiss/100_message.index",
                    Dimension = 1024,
                    VectorCount = 500
                });
                await context.SaveChangesAsync();
            }

            using (var context = new DataDbContext(options)) {
                var file = await context.FaissIndexFiles.FirstAsync();
                Assert.Equal("Message", file.IndexType);
                Assert.Equal(1024, file.Dimension);
                Assert.Equal(500, file.VectorCount);
            }
        }

        [Fact]
        public async Task DataDbContext_ModelCapabilityRelationship() {
            var options = CreateOptions();
            using (var context = new DataDbContext(options)) {
                var channel = new LLMChannel {
                    Name = "OpenAI",
                    Gateway = "https://api.openai.com",
                    ApiKey = "sk-test",
                    Provider = TelegramSearchBot.Model.AI.LLMProvider.OpenAI,
                    Parallel = 5,
                    Priority = 10
                };
                context.LLMChannels.Add(channel);
                await context.SaveChangesAsync();

                var model = new ChannelWithModel {
                    ModelName = "gpt-4",
                    LLMChannelId = channel.Id
                };
                context.ChannelsWithModel.Add(model);
                await context.SaveChangesAsync();

                context.ModelCapabilities.Add(new ModelCapability {
                    ChannelWithModelId = model.Id,
                    CapabilityName = "function_calling",
                    CapabilityValue = "true",
                    Description = "Supports function calling"
                });
                await context.SaveChangesAsync();
            }

            using (var context = new DataDbContext(options)) {
                var model = await context.ChannelsWithModel
                    .Include(m => m.Capabilities)
                    .FirstAsync();
                Assert.Single(model.Capabilities);
                Assert.Equal("function_calling", model.Capabilities.First().CapabilityName);
            }
        }

        [Fact]
        public async Task DataDbContext_AccountBookWithMultipleRecords() {
            var options = CreateOptions();
            using (var context = new DataDbContext(options)) {
                var book = new AccountBook {
                    GroupId = 100,
                    Name = "Multi Record Test",
                    Description = "Test multiple records",
                    CreatedBy = 1
                };
                context.AccountBooks.Add(book);
                await context.SaveChangesAsync();

                context.AccountRecords.Add(new AccountRecord {
                    AccountBookId = book.Id,
                    Amount = 50m,
                    Tag = "test",
                    CreatedBy = 1
                });
                context.AccountRecords.Add(new AccountRecord {
                    AccountBookId = book.Id,
                    Amount = 75m,
                    Tag = "test2",
                    CreatedBy = 1
                });
                await context.SaveChangesAsync();
            }

            using (var context = new DataDbContext(options)) {
                var book = await context.AccountBooks.Include(b => b.Records).FirstAsync();
                Assert.Equal(2, book.Records.Count);
                Assert.Equal(125m, book.Records.Sum(r => r.Amount));
            }
        }

        [Fact]
        public void DataDbContext_AllDbSetsAreAccessible() {
            var options = CreateOptions();
            using var context = new DataDbContext(options);

            Assert.NotNull(context.Messages);
            Assert.NotNull(context.UsersWithGroup);
            Assert.NotNull(context.UserData);
            Assert.NotNull(context.GroupData);
            Assert.NotNull(context.GroupSettings);
            Assert.NotNull(context.LLMChannels);
            Assert.NotNull(context.ChannelsWithModel);
            Assert.NotNull(context.ModelCapabilities);
            Assert.NotNull(context.AppConfigurationItems);
            Assert.NotNull(context.ShortUrlMappings);
            Assert.NotNull(context.TelegramFileCacheEntries);
            Assert.NotNull(context.MessageExtensions);
            Assert.NotNull(context.MemoryGraphs);
            Assert.NotNull(context.SearchPageCaches);
            Assert.NotNull(context.ConversationSegments);
            Assert.NotNull(context.ConversationSegmentMessages);
            Assert.NotNull(context.VectorIndexes);
            Assert.NotNull(context.FaissIndexFiles);
            Assert.NotNull(context.AccountBooks);
            Assert.NotNull(context.AccountRecords);
            Assert.NotNull(context.GroupAccountSettings);
            Assert.NotNull(context.ScheduledTaskExecutions);
        }
    }
}
