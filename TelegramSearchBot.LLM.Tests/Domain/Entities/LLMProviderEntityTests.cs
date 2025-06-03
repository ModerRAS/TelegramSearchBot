using FluentAssertions;
using TelegramSearchBot.LLM.Domain.Entities;
using TelegramSearchBot.LLM.Tests.Common;
using Xunit;

namespace TelegramSearchBot.LLM.Tests.Domain.Entities;

public class LLMProviderEntityTests
{
    public class 当创建LLM提供商实体时 : BddTestBase
    {
        private LLMProviderEntity? _providerEntity;
        private LLMProvider _provider;
        private string _name = null!;

        protected override Task Given()
        {
            _provider = LLMProvider.OpenAI;
            _name = "OpenAI Provider";
            return Task.CompletedTask;
        }

        protected override Task When()
        {
            _providerEntity = new LLMProviderEntity(_provider, _name);
            return Task.CompletedTask;
        }

        protected override Task Then()
        {
            _providerEntity.Should().NotBeNull();
            _providerEntity!.Provider.Should().Be(_provider);
            _providerEntity.Name.Should().Be(_name);
            _providerEntity.IsActive.Should().BeTrue();
            _providerEntity.SupportedCapabilities.Should().BeEmpty();
            return Task.CompletedTask;
        }

        [Fact]
        public async Task 应该成功创建LLM提供商实体()
        {
            await RunTest();
        }
    }

    public class 当添加能力到LLM提供商实体时 : BddTestBase
    {
        private LLMProviderEntity _providerEntity = null!;
        private string _capability = null!;

        protected override Task Given()
        {
            _providerEntity = new LLMProviderEntity(LLMProvider.OpenAI, "OpenAI Provider");
            _capability = "text-generation";
            return Task.CompletedTask;
        }

        protected override Task When()
        {
            _providerEntity.AddCapability(_capability);
            return Task.CompletedTask;
        }

        protected override Task Then()
        {
            _providerEntity.HasCapability(_capability).Should().BeTrue();
            _providerEntity.SupportedCapabilities.Should().Contain(_capability);
            _providerEntity.SupportedCapabilities.Count.Should().Be(1);
            return Task.CompletedTask;
        }

        [Fact]
        public async Task 应该成功添加能力()
        {
            await RunTest();
        }
    }
} 