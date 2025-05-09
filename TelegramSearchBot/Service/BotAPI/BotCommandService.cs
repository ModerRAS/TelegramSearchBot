using Microsoft.Extensions.Hosting; // Added for BackgroundService
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramSearchBot.Attributes; 

namespace TelegramSearchBot.Service.BotAPI
{
    public class BotCommandService : BackgroundService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly ILogger<BotCommandService> _logger;

        public BotCommandService(ITelegramBotClient botClient, ILogger<BotCommandService> logger)
        {
            _botClient = botClient;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("Discovering bot commands using reflection...");
                var discoveredCommands = new List<BotCommand>();
                
                // Get the assembly where BotCommandAttribute and command handlers are defined.
                Assembly assembly = Assembly.GetExecutingAssembly(); 

                foreach (Type type in assembly.GetTypes())
                {
                    // Check for class-level attributes
                    foreach (var attribute in type.GetCustomAttributes<BotCommandAttribute>(false))
                    {
                        discoveredCommands.Add(new BotCommand { Command = attribute.Command, Description = attribute.Description });
                    }
                    // Method-level attributes could be added here if needed
                }

                if (discoveredCommands.Any())
                {
                    _logger.LogInformation($"Found {discoveredCommands.Count} commands. Setting bot commands...");
                    // Use the stoppingToken from ExecuteAsync
                    await _botClient.SetMyCommandsAsync(discoveredCommands.DistinctBy(c => c.Command), cancellationToken: stoppingToken);
                    _logger.LogInformation("Bot commands set successfully via reflection.");
                }
                else
                {
                    _logger.LogWarning("No bot commands found via reflection. Clearing existing commands.");
                    await _botClient.SetMyCommandsAsync(new List<BotCommand>(), cancellationToken: stoppingToken); 
                }
            }
            // It's important to catch OperationCanceledException if the host is shutting down during this operation.
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Bot command registration was canceled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting bot commands using reflection");
            }
            // This service is intended to run once at startup. 
            // BackgroundService will keep running unless ExecuteAsync completes or is cancelled.
            // For a one-off task, ExecuteAsync should complete.
        }
    }
}
