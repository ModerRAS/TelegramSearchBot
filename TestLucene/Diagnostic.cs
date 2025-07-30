using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Extension;

namespace Diagnostic {
    public class DiagnosticProgram {
        public static void RunDiagnostic() {
            Console.WriteLine("=== IOnUpdate Controller Registration Diagnostic ===");
            
            try {
                // Create a service collection and register services
                var services = new ServiceCollection();
                services.ConfigureAllServices();
                
                // Build the service provider
                var serviceProvider = services.BuildServiceProvider();
                
                // Get all IOnUpdate implementations
                var controllers = serviceProvider.GetServices<IOnUpdate>().ToList();
                
                Console.WriteLine($"Total IOnUpdate controllers found: {controllers.Count}");
                
                foreach (var controller in controllers) {
                    Console.WriteLine($"- {controller.GetType().FullName}");
                    var deps = controller.Dependencies;
                    Console.WriteLine($"  Dependencies: {string.Join(", ", deps.Select(d => d.Name))}");
                }
                
                // Check specifically for LuceneIndexController
                var luceneController = controllers.FirstOrDefault(c => c.GetType().Name == "LuceneIndexController");
                if (luceneController != null) {
                    Console.WriteLine("✅ LuceneIndexController is registered");
                } else {
                    Console.WriteLine("❌ LuceneIndexController is NOT registered");
                }
                
                // List all types that implement IOnUpdate
                var assembly = typeof(TelegramSearchBot.AppBootstrap.GeneralBootstrap).Assembly;
                var allOnUpdateTypes = assembly.GetTypes()
                    .Where(t => typeof(IOnUpdate).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                    .ToList();
                
                Console.WriteLine($"\nAll IOnUpdate implementations found in assembly: {allOnUpdateTypes.Count}");
                foreach (var type in allOnUpdateTypes) {
                    Console.WriteLine($"- {type.FullName}");
                }
                
            } catch (Exception ex) {
                Console.WriteLine($"Error during diagnostic: {ex}");
            }
            
            Console.WriteLine("=== End Diagnostic ===");
        }
    }
}