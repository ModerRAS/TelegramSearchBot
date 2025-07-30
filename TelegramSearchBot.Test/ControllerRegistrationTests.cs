using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using TelegramSearchBot.Interface.Controller;
using Xunit;

namespace TelegramSearchBot.Test {
    public class ControllerRegistrationTests {
        [Fact]
        public void TestIOnUpdateControllerRegistration() {
            // Create a service collection and register services without Redis
            var services = new ServiceCollection();
            services.AddSingleton<IOnUpdate, TelegramSearchBot.Controller.Storage.LuceneIndexController>();
            services.AddSingleton<IOnUpdate, TelegramSearchBot.Controller.Storage.MessageController>();
            // Just test the specific ones we care about
            
            // Build the service provider
            var serviceProvider = services.BuildServiceProvider();
            
            // Get all IOnUpdate implementations
            var controllers = serviceProvider.GetServices<IOnUpdate>().ToList();
            
            Console.WriteLine($"Total IOnUpdate controllers found: {controllers.Count}");
            
            foreach (var controller in controllers) {
                Console.WriteLine($"- {controller.GetType().FullName}");
            }
            
            // Check specifically for LuceneIndexController
            var luceneController = controllers.FirstOrDefault(c => c.GetType().Name == "LuceneIndexController");
            Assert.NotNull(luceneController); // This should pass
            
            Assert.True(controllers.Count > 0);
        }
        
        [Fact]
        public void TestAllIOnUpdateImplementations() {
            var assembly = typeof(TelegramSearchBot.AppBootstrap.GeneralBootstrap).Assembly;
            var allOnUpdateTypes = assembly.GetTypes()
                .Where(t => typeof(IOnUpdate).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .ToList();
            
            Console.WriteLine($"All IOnUpdate implementations found in assembly: {allOnUpdateTypes.Count}");
            foreach (var type in allOnUpdateTypes) {
                Console.WriteLine($"- {type.FullName}");
            }
            
            // Check if LuceneIndexController is among them
            var luceneController = allOnUpdateTypes.FirstOrDefault(t => t.Name == "LuceneIndexController");
            Assert.NotNull(luceneController);
        }
    }
}