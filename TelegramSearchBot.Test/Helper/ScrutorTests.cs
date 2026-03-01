using Microsoft.Extensions.DependencyInjection;
using Scrutor;
using Xunit;

namespace TelegramSearchBot.Test.Helper {
    /// <summary>
    /// Tests for Scrutor assembly scanning patterns used in the project.
    /// These tests validate behavior before dependency upgrades.
    /// </summary>
    public class ScrutorTests {
        private interface IScrutorTestService {
            string Name { get; }
        }

        private interface IScrutorAnotherService {
            int Value { get; }
        }

        private class ScrutorServiceA : IScrutorTestService {
            public string Name => "ServiceA";
        }

        private class ScrutorServiceB : IScrutorTestService {
            public string Name => "ServiceB";
        }

        private class ScrutorServiceC : IScrutorAnotherService {
            public int Value => 42;
        }

        [Fact]
        public void Scrutor_Scan_RegistersAllImplementationsOfInterface() {
            var services = new ServiceCollection();
            services.Scan(scan => scan
                .FromAssemblyOf<ScrutorTests>()
                .AddClasses(c => c.AssignableTo<IScrutorTestService>(), publicOnly: false)
                .AsImplementedInterfaces()
                .WithTransientLifetime());

            var provider = services.BuildServiceProvider();
            var implementations = provider.GetServices<IScrutorTestService>();

            Assert.Contains(implementations, s => s.Name == "ServiceA");
            Assert.Contains(implementations, s => s.Name == "ServiceB");
        }

        [Fact]
        public void Scrutor_Scan_RegistersSingleImplementation() {
            var services = new ServiceCollection();
            services.Scan(scan => scan
                .FromAssemblyOf<ScrutorTests>()
                .AddClasses(c => c.AssignableTo<IScrutorAnotherService>(), publicOnly: false)
                .AsImplementedInterfaces()
                .WithSingletonLifetime());

            var provider = services.BuildServiceProvider();
            var service = provider.GetRequiredService<IScrutorAnotherService>();

            Assert.NotNull(service);
            Assert.Equal(42, service.Value);
        }

        [Fact]
        public void Scrutor_Scan_WithScopedLifetime_WorksCorrectly() {
            var services = new ServiceCollection();
            services.Scan(scan => scan
                .FromAssemblyOf<ScrutorTests>()
                .AddClasses(c => c.AssignableTo<IScrutorAnotherService>(), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime());

            var provider = services.BuildServiceProvider();
            using var scope1 = provider.CreateScope();
            using var scope2 = provider.CreateScope();

            var svc1 = scope1.ServiceProvider.GetRequiredService<IScrutorAnotherService>();
            var svc2 = scope2.ServiceProvider.GetRequiredService<IScrutorAnotherService>();

            Assert.NotNull(svc1);
            Assert.NotNull(svc2);
        }
    }
}
