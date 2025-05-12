using Orleans;
using Orleans.Streams;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot; // For ITelegramBotClient and AnswerCallbackQueryAsync
using Telegram.Bot.Types; // For CallbackQuery
using TelegramSearchBot.Interfaces; // For ICallbackQueryHandlerGrain, ISearchQueryGrain
using TelegramSearchBot.Model;    // For StreamMessage, OrleansStreamConstants

namespace TelegramSearchBot.Grains
{
    public class CallbackQueryHandlerGrain : Grain, ICallbackQueryHandlerGrain, IAsyncObserver<StreamMessage<CallbackQuery>>
    {
        private readonly IGrainFactory _grainFactory;
        private readonly ITelegramBotClient _botClient; // To answer callback queries
        private readonly ILogger _logger;
        private IAsyncStream<StreamMessage<CallbackQuery>> _callbackQueryStream;

        public CallbackQueryHandlerGrain(IGrainFactory grainFactory, ITelegramBotClient botClient, ILogger logger)
        {
            _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
            _botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
            _logger = logger?.ForContext<CallbackQueryHandlerGrain>() ?? Log.ForContext<CallbackQueryHandlerGrain>();
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.Information("CallbackQueryHandlerGrain {GrainId} activated.", this.GetPrimaryKeyString()); // Grain key is string

            var streamProvider = this.GetStreamProvider("DefaultSMSProvider");
            _callbackQueryStream = streamProvider.GetStream<StreamMessage<CallbackQuery>>(
                OrleansStreamConstants.RawCallbackQueryMessagesStreamName,
                OrleansStreamConstants.RawMessagesStreamNamespace);
            
            await _callbackQueryStream.SubscribeAsync(this);
            await base.OnActivateAsync(cancellationToken);
        }

        public async Task OnNextAsync(StreamMessage<CallbackQuery> streamMessage, StreamSequenceToken token = null)
        {
            var callbackQuery = streamMessage.Payload;
            if (callbackQuery?.Data == null)
            {
                _logger.Warning("CallbackQueryHandlerGrain received callbackQuery without data. CallbackQueryId: {CallbackQueryId}", callbackQuery?.Id);
                if (callbackQuery != null) await AnswerCallbackQuerySilentlyAsync(callbackQuery.Id);
                return;
            }

            _logger.Information("CallbackQueryHandlerGrain received CallbackQuery.Id: {CallbackQueryId}, Data: \"{CallbackData}\" from User: {UserId}",
                callbackQuery.Id, callbackQuery.Data, callbackQuery.From.Id);

            try
            {
                // Example: "searchgrain:grainIdString:next_page"
                // Example: "searchgrain:grainIdString:go_to_page:3"
                // Example: "searchgrain:grainIdString:noop" (for page indicator button)
                string[] parts = callbackQuery.Data.Split(':');

                if (parts.Length >= 3 && parts[0].Equals("searchgrain", StringComparison.OrdinalIgnoreCase))
                {
                    string targetGrainId = parts[1]; 
                    string action = parts[2];
                    int? pageNumber = null;

                    if (action.Equals("go_to_page", StringComparison.OrdinalIgnoreCase) && parts.Length >= 4)
                    {
                        if (int.TryParse(parts[3], out int parsedPage))
                        {
                            pageNumber = parsedPage;
                        }
                        else
                        {
                            _logger.Warning("CallbackQueryHandlerGrain: Could not parse page number from CallbackData: {CallbackData}", callbackQuery.Data);
                            await AnswerCallbackQuerySilentlyAsync(callbackQuery.Id, "无效的页码格式");
                            return;
                        }
                    } else if (action.Equals("noop", StringComparison.OrdinalIgnoreCase)) 
                    {
                        // No operation needed for "noop", just acknowledge.
                        await AnswerCallbackQuerySilentlyAsync(callbackQuery.Id);
                        return;
                    }
                    
                    var searchQueryGrain = _grainFactory.GetGrain<ISearchQueryGrain>(targetGrainId);
                    await searchQueryGrain.HandlePagingActionAsync(action, pageNumber);
                    await AnswerCallbackQuerySilentlyAsync(callbackQuery.Id); 
                }
                // Add other callback data prefixes here if needed (e.g., "bili:")
                // else if (parts.Length >= 2 && parts[0].Equals("some_other_prefix", StringComparison.OrdinalIgnoreCase)) { ... }
                else
                {
                    _logger.Warning("CallbackQueryHandlerGrain received unhandled CallbackQueryData format: {CallbackData}", callbackQuery.Data);
                    await AnswerCallbackQuerySilentlyAsync(callbackQuery.Id, "未知操作");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing CallbackQuery.Id: {CallbackQueryId}, Data: \"{CallbackData}\"", callbackQuery.Id, callbackQuery.Data);
                await AnswerCallbackQuerySilentlyAsync(callbackQuery.Id, "处理回调时出错");
            }
        }
        
        private async Task AnswerCallbackQuerySilentlyAsync(string callbackQueryId, string text = null)
        {
            try
            {
                await _botClient.AnswerCallbackQueryAsync(callbackQueryId, text: text);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error sending AnswerCallbackQueryAsync for CallbackQueryId: {CallbackQueryId}", callbackQueryId);
            }
        }

        public Task OnCompletedAsync()
        {
            _logger.Information("CallbackQueryHandlerGrain {GrainId} completed stream processing.", this.GetPrimaryKeyString());
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(Exception ex)
        {
            _logger.Error(ex, "CallbackQueryHandlerGrain {GrainId} encountered an error on stream.", this.GetPrimaryKeyString());
            return Task.CompletedTask;
        }

        public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            _logger.Information("CallbackQueryHandlerGrain {GrainId} deactivating. Reason: {Reason}", this.GetPrimaryKeyString(), reason);
            if (_callbackQueryStream != null)
            {
                var subscriptions = await _callbackQueryStream.GetAllSubscriptionHandles();
                foreach (var sub in subscriptions)
                {
                    await sub.UnsubscribeAsync();
                }
            }
            await base.OnDeactivateAsync(reason, cancellationToken);
        }
    }
}
