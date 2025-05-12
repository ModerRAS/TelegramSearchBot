using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using TelegramSearchBot.Interfaces;
using TelegramSearchBot.Model; // For DataDbContext
using TelegramSearchBot.Model.Data; // For GroupSettings

namespace TelegramSearchBot.Service.Common
{
    public class GroupSettingsService : IGroupSettingsService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ITelegramBotClient _botClient;
        private readonly ILogger<GroupSettingsService> _logger;

        public GroupSettingsService(
            IServiceScopeFactory scopeFactory,
            ITelegramBotClient botClient,
            ILogger<GroupSettingsService> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string?> GetLlmModelForChatAsync(long chatId)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();
                var settings = await dbContext.GroupSettings
                                            .AsNoTracking()
                                            .FirstOrDefaultAsync(gs => gs.GroupId == chatId);
                return settings?.LLMModelName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving LLMModelName for ChatId {ChatId}", chatId);
                return null;
            }
        }

        public async Task SetLlmModelForChatAsync(long chatId, string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
            {
                _logger.LogWarning("SetLlmModelForChatAsync called with null or empty modelName for ChatId {ChatId}", chatId);
                // Optionally, treat empty modelName as "unset"
                // For now, we'll just not set an empty name. If unsetting is desired, a different method or value might be used.
                return; 
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();
                var settings = await dbContext.GroupSettings.FirstOrDefaultAsync(gs => gs.GroupId == chatId);

                if (settings != null)
                {
                    settings.LLMModelName = modelName;
                    _logger.LogInformation("Updated LLMModelName to {ModelName} for ChatId {ChatId}", modelName, chatId);
                }
                else
                {
                    settings = new GroupSettings
                    {
                        GroupId = chatId,
                        LLMModelName = modelName,
                        IsManagerGroup = false // Default value, this command doesn't alter IsManagerGroup
                    };
                    dbContext.GroupSettings.Add(settings);
                    _logger.LogInformation("Created new GroupSettings with LLMModelName {ModelName} for ChatId {ChatId}", modelName, chatId);
                }
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting LLMModelName for ChatId {ChatId} to {ModelName}", chatId, modelName);
                // Consider rethrowing or returning a status if the caller needs to know about failure
            }
        }

        public async Task<bool> IsUserChatAdminAsync(long chatId, long userId)
        {
            try
            {
                // Bots cannot be admins of channels in the same way as users,
                // and GetChatAdministratorsAsync might not work or be intended for bots checking other bots.
                // GetChatMemberAsync is more direct for checking a specific user's status.
                var chatMember = await _botClient.GetChatMemberAsync(chatId, userId);
                
                return chatMember.Status == ChatMemberStatus.Administrator ||
                       chatMember.Status == ChatMemberStatus.Creator;
            }
            catch (Exception ex)
            {
                // This can happen if the bot is not a member of the chat, or other API errors.
                _logger.LogError(ex, "Error checking admin status for UserId {UserId} in ChatId {ChatId}", userId, chatId);
                return false; 
            }
        }
    }
}
