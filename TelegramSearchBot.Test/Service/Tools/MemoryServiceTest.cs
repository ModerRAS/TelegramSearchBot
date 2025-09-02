using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Tools;
using Xunit;

namespace TelegramSearchBot.Test.Service.Tools {
    public class MemoryServiceTest : IDisposable {
        private readonly DataDbContext _dbContext;
        private readonly MemoryService _memoryService;
        private readonly Mock<ILogger<MemoryService>> _mockLogger;

        public MemoryServiceTest() {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _dbContext = new DataDbContext(options);
            _mockLogger = new Mock<ILogger<MemoryService>>();
            _memoryService = new MemoryService(_dbContext, _mockLogger.Object);
        }

        public void Dispose() {
            _dbContext?.Dispose();
        }

        [Fact]
        public async Task CreateEntities_WithValidInput_ShouldSucceed() {
            // Arrange
            var chatId = 12345L;
            var arguments = """
            {
                "entities": [
                    {
                        "name": "TestEntity",
                        "entityType": "Person",
                        "observations": ["观察1", "观察2"]
                    }
                ]
            }
            """;
            var toolContext = new ToolContext { ChatId = chatId };

            // Act
            var result = await _memoryService.ProcessMemoryCommandAsync("create_entities", arguments, toolContext);

            // Assert
            Assert.NotNull(result);
            var entities = result as List<Entity>;
            Assert.NotNull(entities);
            Assert.Single(entities);
            Assert.Equal("TestEntity", entities[0].Name);
            Assert.Equal("Person", entities[0].EntityType);
            Assert.Equal(2, entities[0].Observations.Count);

            // Verify data was saved to database
            var savedEntity = await _dbContext.MemoryGraphs
                .FirstOrDefaultAsync(x => x.ChatId == chatId && x.Name == "TestEntity");
            Assert.NotNull(savedEntity);
            Assert.Equal("Person", savedEntity.EntityType);
            Assert.Contains("观察1", savedEntity.Observations);
            Assert.Contains("观察2", savedEntity.Observations);
        }

        [Fact]
        public async Task CreateRelations_WithValidInput_ShouldSucceed() {
            // Arrange
            var chatId = 12345L;
            var arguments = """
            {
                "relations": [
                    {
                        "from": "Person1",
                        "to": "Person2",
                        "relationType": "朋友"
                    }
                ]
            }
            """;
            var toolContext = new ToolContext { ChatId = chatId };

            // Act
            var result = await _memoryService.ProcessMemoryCommandAsync("create_relations", arguments, toolContext);

            // Assert
            Assert.NotNull(result);
            var relations = result as List<Relation>;
            Assert.NotNull(relations);
            Assert.Single(relations);
            Assert.Equal("Person1", relations[0].From);
            Assert.Equal("Person2", relations[0].To);
            Assert.Equal("朋友", relations[0].RelationType);

            // Verify data was saved to database
            var savedRelation = await _dbContext.MemoryGraphs
                .FirstOrDefaultAsync(x => x.ChatId == chatId && x.ItemType == "relation");
            Assert.NotNull(savedRelation);
            Assert.Equal("Person1", savedRelation.FromEntity);
            Assert.Equal("Person2", savedRelation.ToEntity);
            Assert.Equal("朋友", savedRelation.RelationType);
        }

        [Fact]
        public async Task SearchNodes_WithValidQuery_ShouldReturnFilteredResults() {
            // Arrange
            var chatId = 12345L;

            // First create some test data
            await _dbContext.MemoryGraphs.AddRangeAsync(
                new MemoryGraph {
                    ChatId = chatId,
                    Name = "张三",
                    EntityType = "Person",
                    Observations = "工程师|||喜欢编程",
                    ItemType = "entity",
                    CreatedTime = DateTime.UtcNow
                },
                new MemoryGraph {
                    ChatId = chatId,
                    Name = "李四",
                    EntityType = "Person",
                    Observations = "设计师|||喜欢艺术",
                    ItemType = "entity",
                    CreatedTime = DateTime.UtcNow
                }
            );
            await _dbContext.SaveChangesAsync();

            var arguments = """
            {
                "query": "工程师"
            }
            """;
            var toolContext = new ToolContext { ChatId = chatId };

            // Act
            var result = await _memoryService.ProcessMemoryCommandAsync("search_nodes", arguments, toolContext);

            // Assert
            Assert.NotNull(result);
            var graph = result as KnowledgeGraph;
            Assert.NotNull(graph);
            Assert.Single(graph.Entities);
            Assert.Equal("张三", graph.Entities[0].Name);
            Assert.Contains("工程师", graph.Entities[0].Observations);
        }

        [Fact]
        public async Task ReadGraph_ShouldReturnAllEntitiesAndRelations() {
            // Arrange
            var chatId = 12345L;

            // Create test data
            await _dbContext.MemoryGraphs.AddRangeAsync(
                new MemoryGraph {
                    ChatId = chatId,
                    Name = "实体1",
                    EntityType = "类型1",
                    ItemType = "entity",
                    CreatedTime = DateTime.UtcNow
                },
                new MemoryGraph {
                    ChatId = chatId,
                    Name = "实体1-关系-实体2",
                    EntityType = "Relation", // Required field for relation type as well
                    FromEntity = "实体1",
                    ToEntity = "实体2",
                    RelationType = "关联",
                    ItemType = "relation",
                    CreatedTime = DateTime.UtcNow
                }
            );
            await _dbContext.SaveChangesAsync();

            var arguments = "{}";
            var toolContext = new ToolContext { ChatId = chatId };

            // Act
            var result = await _memoryService.ProcessMemoryCommandAsync("read_graph", arguments, toolContext);

            // Assert
            Assert.NotNull(result);
            var graph = result as KnowledgeGraph;
            Assert.NotNull(graph);
            Assert.Single(graph.Entities);
            Assert.Single(graph.Relations);
        }

        [Fact]
        public async Task ProcessMemoryCommandAsync_WithInvalidJson_ShouldThrowException() {
            // Arrange
            var chatId = 12345L;
            var invalidJson = "{ invalid json }";
            var toolContext = new ToolContext { ChatId = chatId };

            // Act & Assert
            var ex = await Assert.ThrowsAsync<Exception>(async () =>
                await _memoryService.ProcessMemoryCommandAsync("create_entities", invalidJson, toolContext));

            Assert.Contains("Invalid JSON format", ex.Message);
        }

        [Fact]
        public async Task ProcessMemoryCommandAsync_WithUnknownCommand_ShouldThrowException() {
            // Arrange
            var chatId = 12345L;
            var arguments = "{}";
            var toolContext = new ToolContext { ChatId = chatId };

            // Act & Assert
            var ex = await Assert.ThrowsAsync<Exception>(async () =>
                await _memoryService.ProcessMemoryCommandAsync("unknown_command", arguments, toolContext));

            Assert.Contains("Unknown command", ex.Message);
        }
    }
}
