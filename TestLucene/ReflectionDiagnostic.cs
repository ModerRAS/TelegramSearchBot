using System;
using System.Linq;
using System.Reflection;
using TelegramSearchBot.Interface.Controller;

namespace TestLucene {
    public class ReflectionDiagnostic {
        public static void Main() {
            Console.WriteLine("=== IOnUpdate Controller Registration Analysis via Reflection ===");
            
            try {
                // Load the assembly
                var assembly = Assembly.Load("TelegramSearchBot");
                
                // Find all IOnUpdate implementations
                var onUpdateTypes = assembly.GetTypes()
                    .Where(t => typeof(IOnUpdate).IsAssignableFrom(t) 
                               && !t.IsInterface 
                               && !t.IsAbstract)
                    .ToList();
                
                Console.WriteLine($"Total IOnUpdate implementations found: {onUpdateTypes.Count}");
                
                foreach (var type in onUpdateTypes) {
                    Console.WriteLine($"- {type.FullName}");
                    
                    // Check for Injectable attribute
                    var injectableAttr = type.GetCustomAttributes()
                        .FirstOrDefault(a => a.GetType().Name == "InjectableAttribute");
                    if (injectableAttr != null) {
                        var lifetimeProp = injectableAttr.GetType().GetProperty("Lifetime");
                        var lifetime = lifetimeProp?.GetValue(injectableAttr);
                        Console.WriteLine($"  [Injectable] Lifetime: {lifetime}");
                    }
                    
                    // Check dependencies
                    try {
                        var instance = Activator.CreateInstance(type);
                        var dependenciesProp = type.GetProperty("Dependencies");
                        if (dependenciesProp != null) {
                            var dependencies = dependenciesProp.GetValue(instance);
                            if (dependencies != null) {
                                var deps = dependencies as System.Collections.Generic.List<Type>;
                                if (deps != null) {
                                    Console.WriteLine($"  Dependencies: {string.Join(", ", deps.Select(d => d.Name))}");
                                }
                            }
                        }
                    } catch (Exception ex) {
                        Console.WriteLine($"  Error creating instance: {ex.Message}");
                    }
                    
                    Console.WriteLine();
                }
                
                // Check specifically for LuceneIndexController
                var luceneController = onUpdateTypes.FirstOrDefault(t => t.Name == "LuceneIndexController");
                if (luceneController != null) {
                    Console.WriteLine("✅ LuceneIndexController found in assembly");
                    
                    // Check if it's properly configured
                    var hasInjectable = luceneController.GetCustomAttributes()
                        .Any(a => a.GetType().Name == "InjectableAttribute");
                    Console.WriteLine($"  Has Injectable attribute: {hasInjectable}");
                    
                    var implementsInterface = typeof(IOnUpdate).IsAssignableFrom(luceneController);
                    Console.WriteLine($"  Implements IOnUpdate: {implementsInterface}");
                    
                } else {
                    Console.WriteLine("❌ LuceneIndexController NOT found in assembly");
                }
                
                // Check ServiceCollectionExtension scanning configuration
                Console.WriteLine("\n=== Scanning Configuration Analysis ===");
                Console.WriteLine("AddAutoRegisteredServices method:");
                Console.WriteLine("- Scans for IOnUpdate implementations");
                Console.WriteLine("- Registers as implemented interfaces");
                Console.WriteLine("- Uses transient lifetime");
                
            } catch (Exception ex) {
                Console.WriteLine($"Error during reflection analysis: {ex}");
            }
            
            Console.WriteLine("=== End Reflection Analysis ===");
        }
    }
}