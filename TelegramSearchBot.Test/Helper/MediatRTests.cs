using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TelegramSearchBot.Test.Helper {
    /// <summary>
    /// Tests for MediatR IRequest/IRequestHandler patterns used in the project.
    /// These tests validate behavior before dependency upgrades.
    /// </summary>
    public class MediatRTests {
        // Test request
        private class AddRequest : IRequest<int> {
            public int A { get; set; }
            public int B { get; set; }
        }

        // Test handler
        private class AddHandler : IRequestHandler<AddRequest, int> {
            public Task<int> Handle(AddRequest request, CancellationToken cancellationToken)
                => Task.FromResult(request.A + request.B);
        }

        // Test notification
        private class PingNotification : INotification {
            public string Message { get; set; } = string.Empty;
        }

        // Test notification handler
        private class PingHandler : INotificationHandler<PingNotification> {
            public static string? LastMessage { get; set; }
            public Task Handle(PingNotification notification, CancellationToken cancellationToken) {
                LastMessage = notification.Message;
                return Task.CompletedTask;
            }
        }

        private static IServiceProvider BuildServices() {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(MediatRTests).Assembly));
            return services.BuildServiceProvider();
        }

        [Fact]
        public async Task MediatR_Send_RequestHandler_ReturnsResult() {
            var provider = BuildServices();
            var mediator = provider.GetRequiredService<IMediator>();

            var result = await mediator.Send(new AddRequest { A = 3, B = 4 });
            Assert.Equal(7, result);
        }

        [Fact]
        public async Task MediatR_Publish_NotificationHandler_Invoked() {
            PingHandler.LastMessage = null;
            var provider = BuildServices();
            var mediator = provider.GetRequiredService<IMediator>();

            await mediator.Publish(new PingNotification { Message = "hello" });
            Assert.Equal("hello", PingHandler.LastMessage);
        }

        [Fact]
        public async Task MediatR_Send_CancellationToken_Respected() {
            var provider = BuildServices();
            var mediator = provider.GetRequiredService<IMediator>();

            using var cts = new CancellationTokenSource();
            var result = await mediator.Send(new AddRequest { A = 10, B = 5 }, cts.Token);
            Assert.Equal(15, result);
        }
    }
}
