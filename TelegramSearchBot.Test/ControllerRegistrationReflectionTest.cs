using System;
using System.Linq;
using System.Reflection;
using TelegramSearchBot.Interface.Controller;
using Xunit;

namespace TelegramSearchBot.Test {
    public class ControllerRegistrationReflectionTest {
        [Fact]
        public void TestLuceneIndexControllerExistsAndImplementsInterface() {
            var assembly = typeof(TelegramSearchBot.AppBootstrap.GeneralBootstrap).Assembly;
            
            var luceneController = assembly.GetType("TelegramSearchBot.Controller.Storage.LuceneIndexController");
            Assert.NotNull(luceneController);
            
            var implementsIOnUpdate = typeof(IOnUpdate).IsAssignableFrom(luceneController);
            Assert.True(implementsIOnUpdate);
            
            var hasInjectable = luceneController.GetCustomAttributes()
                .Any(a => a.GetType().Name == "InjectableAttribute");
            Assert.True(hasInjectable);
            
            Console.WriteLine($"✅ LuceneIndexController found and properly configured");
            Console.WriteLine($"   Type: {luceneController.FullName}");
            Console.WriteLine($"   Implements IOnUpdate: {implementsIOnUpdate}");
            Console.WriteLine($"   Has Injectable attribute: {hasInjectable}");
        }
        
        [Fact]
        public void TestIOnUpdateImplementationsCount() {
            var assembly = typeof(TelegramSearchBot.AppBootstrap.GeneralBootstrap).Assembly;
            var allOnUpdateTypes = assembly.GetTypes()
                .Where(t => typeof(IOnUpdate).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .ToList();
            
            Console.WriteLine($"Found {allOnUpdateTypes.Count} IOnUpdate implementations:");
            foreach (var type in allOnUpdateTypes) {
                Console.WriteLine($"- {type.FullName}");
            }
            
            Assert.True(allOnUpdateTypes.Count > 0);
            Assert.Contains(allOnUpdateTypes, t => t.Name == "LuceneIndexController");
        }
        
        [Fact]
        public void TestAutoRegistrationMechanism() {
            // Test that the scanning mechanism should find LuceneIndexController
            var assembly = typeof(TelegramSearchBot.AppBootstrap.GeneralBootstrap).Assembly;
            
            // Look for all classes that implement IOnUpdate
            var onUpdateTypes = assembly.GetTypes()
                .Where(t => typeof(IOnUpdate).IsAssignableFrom(t) 
                           && !t.IsInterface 
                           && !t.IsAbstract
                           && t.GetCustomAttributes()
                               .Any(a => a.GetType().Name == "InjectableAttribute"))
                .ToList();
            
            Console.WriteLine($"Classes implementing IOnUpdate with Injectable attribute: {onUpdateTypes.Count}");
            foreach (var type in onUpdateTypes) {
                Console.WriteLine($"- {type.FullName}");
            }
            
            Assert.Contains(onUpdateTypes, t => t.Name == "LuceneIndexController");
        }
    }
}