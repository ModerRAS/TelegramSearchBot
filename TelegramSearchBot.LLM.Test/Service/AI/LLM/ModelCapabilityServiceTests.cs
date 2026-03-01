#pragma warning disable CS8602 // Dereference of a possibly null reference
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.AI.LLM;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.AI.LLM;
using Xunit;

namespace TelegramSearchBot.Test.Service.AI.LLM {
    public class ModelCapabilityServiceTests {
        private readonly DataDbContext _dbContext;
        private readonly Mock<ILogger<ModelCapabilityService>> _loggerMock;
        private readonly Mock<IServiceProvider> _serviceProviderMock;
        private readonly ModelCapabilityService _service;

        public ModelCapabilityServiceTests() {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new DataDbContext(options);
            _loggerMock = new Mock<ILogger<ModelCapabilityService>>();
            _serviceProviderMock = new Mock<IServiceProvider>();

            _service = new ModelCapabilityService(
                _loggerMock.Object,
                _dbContext,
                _serviceProviderMock.Object);
        }

        [Fact]
        public void ServiceName_ReturnsExpectedName() {
            Assert.Equal("ModelCapabilityService", _service.ServiceName);
        }

        [Fact]
        public void Service_ImplementsIModelCapabilityService() {
            Assert.IsAssignableFrom<IModelCapabilityService>(_service);
        }

        [Fact]
        public void Service_ImplementsIService() {
            Assert.IsAssignableFrom<IService>(_service);
        }

        [Fact]
        public async Task UpdateChannelModelCapabilities_ChannelNotFound_ReturnsFalse() {
            var result = await _service.UpdateChannelModelCapabilities(999);
            Assert.False(result);
        }

        [Fact]
        public async Task GetModelCapabilities_NotFound_ReturnsNull() {
            var result = await _service.GetModelCapabilities("nonexistent", 999);
            Assert.Null(result);
        }

        [Fact]
        public async Task GetModelCapabilities_WithCapabilities_ReturnsCorrectModel() {
            // Arrange
            var channel = new LLMChannel {
                Name = "test", Gateway = "gw", ApiKey = "key",
                Provider = LLMProvider.OpenAI, Parallel = 1, Priority = 1
            };
            _dbContext.LLMChannels.Add(channel);
            await _dbContext.SaveChangesAsync();

            var cwm = new ChannelWithModel {
                ModelName = "gpt-4",
                LLMChannelId = channel.Id,
                Capabilities = new List<ModelCapability> {
                    new ModelCapability {
                        CapabilityName = "function_calling",
                        CapabilityValue = "true",
                        LastUpdated = DateTime.UtcNow
                    },
                    new ModelCapability {
                        CapabilityName = "vision",
                        CapabilityValue = "true",
                        LastUpdated = DateTime.UtcNow
                    }
                }
            };
            _dbContext.ChannelsWithModel.Add(cwm);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _service.GetModelCapabilities("gpt-4", channel.Id);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("gpt-4", result.ModelName);
            Assert.True(result.SupportsToolCalling);
            Assert.True(result.SupportsVision);
        }

        [Fact]
        public async Task GetToolCallingSupportedModels_ReturnsCorrectModels() {
            // Arrange
            var channel = new LLMChannel {
                Name = "test", Gateway = "gw", ApiKey = "key",
                Provider = LLMProvider.OpenAI, Parallel = 1, Priority = 1
            };
            _dbContext.LLMChannels.Add(channel);
            await _dbContext.SaveChangesAsync();

            var cwm1 = new ChannelWithModel {
                ModelName = "gpt-4",
                LLMChannelId = channel.Id,
                Capabilities = new List<ModelCapability> {
                    new ModelCapability {
                        CapabilityName = "function_calling",
                        CapabilityValue = "true",
                        LastUpdated = DateTime.UtcNow
                    }
                }
            };
            var cwm2 = new ChannelWithModel {
                ModelName = "text-embedding-3-small",
                LLMChannelId = channel.Id,
                Capabilities = new List<ModelCapability> {
                    new ModelCapability {
                        CapabilityName = "embedding",
                        CapabilityValue = "true",
                        LastUpdated = DateTime.UtcNow
                    }
                }
            };
            _dbContext.ChannelsWithModel.AddRange(cwm1, cwm2);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = (await _service.GetToolCallingSupportedModels()).ToList();

            // Assert
            Assert.Single(result);
            Assert.Equal("gpt-4", result[0].ModelName);
        }

        [Fact]
        public async Task GetVisionSupportedModels_ReturnsCorrectModels() {
            // Arrange
            var channel = new LLMChannel {
                Name = "test", Gateway = "gw", ApiKey = "key",
                Provider = LLMProvider.OpenAI, Parallel = 1, Priority = 1
            };
            _dbContext.LLMChannels.Add(channel);
            await _dbContext.SaveChangesAsync();

            var cwm = new ChannelWithModel {
                ModelName = "gpt-4-vision",
                LLMChannelId = channel.Id,
                Capabilities = new List<ModelCapability> {
                    new ModelCapability {
                        CapabilityName = "vision",
                        CapabilityValue = "true",
                        LastUpdated = DateTime.UtcNow
                    }
                }
            };
            _dbContext.ChannelsWithModel.Add(cwm);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = (await _service.GetVisionSupportedModels()).ToList();

            // Assert
            Assert.Single(result);
            Assert.Equal("gpt-4-vision", result[0].ModelName);
        }

        [Fact]
        public async Task GetEmbeddingModels_ReturnsCorrectModels() {
            // Arrange
            var channel = new LLMChannel {
                Name = "test", Gateway = "gw", ApiKey = "key",
                Provider = LLMProvider.OpenAI, Parallel = 1, Priority = 1
            };
            _dbContext.LLMChannels.Add(channel);
            await _dbContext.SaveChangesAsync();

            var cwm = new ChannelWithModel {
                ModelName = "text-embedding-3-small",
                LLMChannelId = channel.Id,
                Capabilities = new List<ModelCapability> {
                    new ModelCapability {
                        CapabilityName = "embedding",
                        CapabilityValue = "true",
                        LastUpdated = DateTime.UtcNow
                    }
                }
            };
            _dbContext.ChannelsWithModel.Add(cwm);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = (await _service.GetEmbeddingModels()).ToList();

            // Assert
            Assert.Single(result);
            Assert.Equal("text-embedding-3-small", result[0].ModelName);
        }

        [Fact]
        public async Task CleanupOldCapabilities_RemovesOldEntries() {
            // Arrange
            var channel = new LLMChannel {
                Name = "test", Gateway = "gw", ApiKey = "key",
                Provider = LLMProvider.OpenAI, Parallel = 1, Priority = 1
            };
            _dbContext.LLMChannels.Add(channel);
            await _dbContext.SaveChangesAsync();

            var cwm = new ChannelWithModel {
                ModelName = "gpt-4",
                LLMChannelId = channel.Id
            };
            _dbContext.ChannelsWithModel.Add(cwm);
            await _dbContext.SaveChangesAsync();

            // Add old capability
            _dbContext.ModelCapabilities.Add(new ModelCapability {
                ChannelWithModelId = cwm.Id,
                CapabilityName = "old_cap",
                CapabilityValue = "true",
                LastUpdated = DateTime.UtcNow.AddDays(-60)
            });
            // Add new capability
            _dbContext.ModelCapabilities.Add(new ModelCapability {
                ChannelWithModelId = cwm.Id,
                CapabilityName = "new_cap",
                CapabilityValue = "true",
                LastUpdated = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync();

            // Act
            var removed = await _service.CleanupOldCapabilities(30);

            // Assert
            Assert.Equal(1, removed);
            var remaining = await _dbContext.ModelCapabilities.ToListAsync();
            Assert.Single(remaining);
            Assert.Equal("new_cap", remaining[0].CapabilityName);
        }

        [Fact]
        public async Task UpdateAllChannelsModelCapabilities_NoChannels_ReturnsZero() {
            var result = await _service.UpdateAllChannelsModelCapabilities();
            Assert.Equal(0, result);
        }

        [Fact]
        public async Task GetModelsByCapability_NoMatches_ReturnsEmpty() {
            var result = await _service.GetModelsByCapability("nonexistent");
            Assert.Empty(result);
        }
    }
}
