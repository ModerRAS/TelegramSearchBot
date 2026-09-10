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
        private readonly Mock<OpenAIService> _openAIServiceMock;
        private readonly ModelCapabilityService _service;

        public ModelCapabilityServiceTests() {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new DataDbContext(options);
            _loggerMock = new Mock<ILogger<ModelCapabilityService>>();
            _serviceProviderMock = new Mock<IServiceProvider>();

            var messageExtensionServiceMock = new Mock<IMessageExtensionService>();
            _openAIServiceMock = new Mock<OpenAIService>(
                _dbContext,
                new Mock<ILogger<OpenAIService>>().Object,
                messageExtensionServiceMock.Object,
                new Mock<IHttpClientFactory>().Object);
            _serviceProviderMock.Setup(sp => sp.GetService(typeof(OpenAIService)))
                .Returns(_openAIServiceMock.Object);

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
                Name = "test",
                Gateway = "gw",
                ApiKey = "key",
                Provider = LLMProvider.OpenAI,
                Parallel = 1,
                Priority = 1
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
                Name = "test",
                Gateway = "gw",
                ApiKey = "key",
                Provider = LLMProvider.OpenAI,
                Parallel = 1,
                Priority = 1
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
            var result = ( await _service.GetToolCallingSupportedModels() ).ToList();

            // Assert
            Assert.Single(result);
            Assert.Equal("gpt-4", result[0].ModelName);
        }

        [Fact]
        public async Task GetVisionSupportedModels_ReturnsCorrectModels() {
            // Arrange
            var channel = new LLMChannel {
                Name = "test",
                Gateway = "gw",
                ApiKey = "key",
                Provider = LLMProvider.OpenAI,
                Parallel = 1,
                Priority = 1
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
            var result = ( await _service.GetVisionSupportedModels() ).ToList();

            // Assert
            Assert.Single(result);
            Assert.Equal("gpt-4-vision", result[0].ModelName);
        }

        [Fact]
        public async Task GetEmbeddingModels_ReturnsCorrectModels() {
            // Arrange
            var channel = new LLMChannel {
                Name = "test",
                Gateway = "gw",
                ApiKey = "key",
                Provider = LLMProvider.OpenAI,
                Parallel = 1,
                Priority = 1
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
            var result = ( await _service.GetEmbeddingModels() ).ToList();

            // Assert
            Assert.Single(result);
            Assert.Equal("text-embedding-3-small", result[0].ModelName);
        }

        [Fact]
        public async Task GetImageGenerationModels_UsesCapabilitiesAndKnownNames() {
            var channel = new LLMChannel {
                Name = "openai",
                Gateway = "gw",
                ApiKey = "key",
                Provider = LLMProvider.OpenAI,
                Parallel = 1,
                Priority = 1
            };
            var minimaxChannel = new LLMChannel {
                Name = "minimax",
                Gateway = "gw",
                ApiKey = "key",
                Provider = LLMProvider.MiniMax,
                Parallel = 1,
                Priority = 1
            };
            _dbContext.LLMChannels.AddRange(channel, minimaxChannel);
            await _dbContext.SaveChangesAsync();

            _dbContext.ChannelsWithModel.AddRange(
                new ChannelWithModel {
                    ModelName = "custom-image-model",
                    LLMChannelId = channel.Id,
                    Capabilities = new List<ModelCapability> {
                        new ModelCapability {
                            CapabilityName = "image_generation",
                            CapabilityValue = "true",
                            LastUpdated = DateTime.UtcNow
                        }
                    }
                },
                new ChannelWithModel {
                    ModelName = "gpt-image-2",
                    LLMChannelId = channel.Id
                },
                new ChannelWithModel {
                    ModelName = "image-01",
                    LLMChannelId = minimaxChannel.Id
                },
                new ChannelWithModel {
                    ModelName = "gpt-4o",
                    LLMChannelId = channel.Id,
                    Capabilities = new List<ModelCapability> {
                        new ModelCapability {
                            CapabilityName = "vision",
                            CapabilityValue = "true",
                            LastUpdated = DateTime.UtcNow
                        }
                    }
                });
            await _dbContext.SaveChangesAsync();

            var result = ( await _service.GetImageGenerationModels() )
                .Select(x => x.ModelName)
                .OrderBy(x => x)
                .ToList();

            Assert.Equal(new[] { "custom-image-model", "gpt-image-2", "image-01" }, result);
        }

        [Fact]
        public async Task GetMusicGenerationModels_UsesCapabilitiesAndKnownNames() {
            var channel = new LLMChannel {
                Name = "openai",
                Gateway = "gw",
                ApiKey = "key",
                Provider = LLMProvider.OpenAI,
                Parallel = 1,
                Priority = 1
            };
            var minimaxChannel = new LLMChannel {
                Name = "minimax",
                Gateway = "gw",
                ApiKey = "key",
                Provider = LLMProvider.MiniMax,
                Parallel = 1,
                Priority = 1
            };
            _dbContext.LLMChannels.AddRange(channel, minimaxChannel);
            await _dbContext.SaveChangesAsync();

            _dbContext.ChannelsWithModel.AddRange(
                new ChannelWithModel {
                    ModelName = "custom-music-model",
                    LLMChannelId = channel.Id,
                    Capabilities = new List<ModelCapability> {
                        new ModelCapability {
                            CapabilityName = "music_generation",
                            CapabilityValue = "true",
                            LastUpdated = DateTime.UtcNow
                        }
                    }
                },
                new ChannelWithModel {
                    ModelName = "music-2.6",
                    LLMChannelId = minimaxChannel.Id
                },
                new ChannelWithModel {
                    ModelName = "music-cover-free",
                    LLMChannelId = minimaxChannel.Id
                },
                new ChannelWithModel {
                    ModelName = "gpt-4o",
                    LLMChannelId = channel.Id,
                    Capabilities = new List<ModelCapability> {
                        new ModelCapability {
                            CapabilityName = "vision",
                            CapabilityValue = "true",
                            LastUpdated = DateTime.UtcNow
                        }
                    }
                });
            await _dbContext.SaveChangesAsync();

            var result = ( await _service.GetMusicGenerationModels() )
                .Select(x => x.ModelName)
                .OrderBy(x => x)
                .ToList();

            Assert.Equal(new[] { "custom-music-model", "music-2.6", "music-cover-free" }, result);
        }

        [Fact]
        public async Task CleanupOldCapabilities_RemovesOldEntries() {
            // Arrange
            var channel = new LLMChannel {
                Name = "test",
                Gateway = "gw",
                ApiKey = "key",
                Provider = LLMProvider.OpenAI,
                Parallel = 1,
                Priority = 1
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

        // ===== Phase 3: metadata 不创建/不复活授权行（blueprint §四.6） =====

        [Fact]
        public async Task UpdateChannelModelCapabilities_MetadataDoesNotCreateRow() {
            // Arrange: 渠道存在，但没有任何模型行；metadata 返回 phantom-model
            var channel = new LLMChannel {
                Name = "openai",
                Gateway = "gw",
                ApiKey = "key",
                Provider = LLMProvider.OpenAI,
                Parallel = 1,
                Priority = 1
            };
            _dbContext.LLMChannels.Add(channel);
            await _dbContext.SaveChangesAsync();

            _openAIServiceMock.Setup(s => s.GetAllModelsWithCapabilities(It.IsAny<LLMChannel>()))
                .ReturnsAsync(new List<ModelWithCapabilities> {
                    new ModelWithCapabilities { ModelName = "phantom-model" }
                });

            // Act
            var result = await _service.UpdateChannelModelCapabilities(channel.Id);

            // Assert: 不创建新行、不产生能力记录
            Assert.True(result);
            Assert.Empty(await _dbContext.ChannelsWithModel.ToListAsync());
            Assert.Empty(await _dbContext.ModelCapabilities.ToListAsync());
        }

        [Fact]
        public async Task UpdateChannelModelCapabilities_MetadataDoesNotResurrect() {
            // Arrange: 已软删除的模型行，metadata 返回同名模型
            var channel = new LLMChannel {
                Name = "openai",
                Gateway = "gw",
                ApiKey = "key",
                Provider = LLMProvider.OpenAI,
                Parallel = 1,
                Priority = 1
            };
            _dbContext.LLMChannels.Add(channel);
            await _dbContext.SaveChangesAsync();

            var cwm = new ChannelWithModel {
                ModelName = "gpt-4o",
                LLMChannelId = channel.Id,
                IsDeleted = true
            };
            _dbContext.ChannelsWithModel.Add(cwm);
            await _dbContext.SaveChangesAsync();

            var modelWithCaps = new ModelWithCapabilities { ModelName = "gpt-4o" };
            modelWithCaps.SetCapability("function_calling", "true");
            _openAIServiceMock.Setup(s => s.GetAllModelsWithCapabilities(It.IsAny<LLMChannel>()))
                .ReturnsAsync(new List<ModelWithCapabilities> { modelWithCaps });

            // Act
            var result = await _service.UpdateChannelModelCapabilities(channel.Id);

            // Assert: 行保持已删除，且未写入能力
            Assert.True(result);
            var loaded = await _dbContext.ChannelsWithModel.SingleAsync();
            Assert.True(loaded.IsDeleted);
            Assert.Empty(await _dbContext.ModelCapabilities.ToListAsync());
        }

        [Fact]
        public async Task UpdateChannelModelCapabilities_MetadataMergesCaseInsensitive() {
            // Arrange: 已存在的非删除行 gpt-4o；metadata 以 GPT-4O 返回 → 忽略大小写合并
            var channel = new LLMChannel {
                Name = "openai",
                Gateway = "gw",
                ApiKey = "key",
                Provider = LLMProvider.OpenAI,
                Parallel = 1,
                Priority = 1
            };
            _dbContext.LLMChannels.Add(channel);
            await _dbContext.SaveChangesAsync();

            var cwm = new ChannelWithModel {
                ModelName = "gpt-4o",
                LLMChannelId = channel.Id,
                IsDeleted = false
            };
            _dbContext.ChannelsWithModel.Add(cwm);
            await _dbContext.SaveChangesAsync();

            var modelWithCaps = new ModelWithCapabilities { ModelName = "GPT-4O" };
            modelWithCaps.SetCapability("function_calling", "true");
            _openAIServiceMock.Setup(s => s.GetAllModelsWithCapabilities(It.IsAny<LLMChannel>()))
                .ReturnsAsync(new List<ModelWithCapabilities> { modelWithCaps });

            // Act
            var result = await _service.UpdateChannelModelCapabilities(channel.Id);

            // Assert: 能力合并到现有行
            Assert.True(result);
            var caps = await _dbContext.ModelCapabilities.ToListAsync();
            Assert.Single(caps);
            Assert.Equal(cwm.Id, caps[0].ChannelWithModelId);
            Assert.Equal("function_calling", caps[0].CapabilityName);
            Assert.Equal("true", caps[0].CapabilityValue);
        }
    }
}
