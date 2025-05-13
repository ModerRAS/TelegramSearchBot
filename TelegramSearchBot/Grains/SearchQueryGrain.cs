using Orleans;
using Orleans.Runtime; // For IPersistentState
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading; // Added for CancellationToken
using System.Threading.Tasks;
using Telegram.Bot.Types.ReplyMarkups; // For InlineKeyboardMarkup, InlineKeyboardButton
using TelegramSearchBot.Interfaces;    // For ISearchQueryGrain, ITelegramMessageSenderGrain, SearchQueryState
using TelegramSearchBot.Intrerface;  // For ISearchService (Note: still using "Intrerface")
using TelegramSearchBot.Model;       // For SearchOption, Data.Message

namespace TelegramSearchBot.Grains
{
    public class SearchQueryGrain : Grain, ISearchQueryGrain
    {
        private readonly IPersistentState<SearchQueryState> _state;
        private readonly ISearchService _searchService;
        private readonly IGrainFactory _grainFactory;
        private readonly ILogger _logger;

        public SearchQueryGrain(
            [PersistentState("searchQueryState", "DefaultGrainStorage")] IPersistentState<SearchQueryState> state,
            ISearchService searchService,
            IGrainFactory grainFactory,
            ILogger logger)
        {
            _state = state;
            _searchService = searchService;
            _grainFactory = grainFactory;
            _logger = logger?.ForContext<SearchQueryGrain>() ?? Log.ForContext<SearchQueryGrain>();
        }

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            // Initialize state if it's new
            if (_state.State == null)
            {
                _state.State = new SearchQueryState();
            }
            return base.OnActivateAsync(cancellationToken);
        }

        public async Task StartSearchAsync(string query, long chatId, int messageId, long userId)
        {
            _logger.Information("Grain {GrainId}: StartSearchAsync called with query '{Query}' for ChatId {ChatId}, UserId {UserId}",
                this.GetPrimaryKeyString(), query, chatId, userId);

            _state.State.OriginalQuery = query;
            _state.State.CurrentPage = 1;
            // _state.State.PageSize is defaulted in SearchQueryState definition
            _state.State.InitiatingChatId = chatId;
            _state.State.InitiatingMessageId = messageId;
            _state.State.InitiatingUserId = userId;
            _state.State.TotalResults = -1; // Indicate first search
            _state.State.SearchResultMessageId = 0; // Reset previous result message ID

            // Optionally, send a "Searching..." message immediately
            // var sender = _grainFactory.GetGrain<ITelegramMessageSenderGrain>(0);
            // await sender.SendMessageAsync(new TelegramMessageToSend { ChatId = chatId, Text = $"正在搜索 \"{query}\"...", ReplyToMessageId = messageId });

            await ExecuteSearchAndRespondAsync();
        }

        public async Task HandlePagingActionAsync(string action, int? pageNumber = null)
        {
            _logger.Information("Grain {GrainId}: HandlePagingActionAsync called with action '{Action}', PageNumber: {PageNumber}",
                this.GetPrimaryKeyString(), action, pageNumber);

            bool stateChanged = false;
            switch (action?.ToLowerInvariant())
            {
                case "next_page":
                    if ((_state.State.CurrentPage * _state.State.PageSize) < _state.State.TotalResults)
                    {
                        _state.State.CurrentPage++;
                        stateChanged = true;
                    }
                    break;
                case "prev_page":
                    if (_state.State.CurrentPage > 1)
                    {
                        _state.State.CurrentPage--;
                        stateChanged = true;
                    }
                    break;
                case "go_to_page":
                    if (pageNumber.HasValue && pageNumber.Value > 0 && 
                        ((pageNumber.Value - 1) * _state.State.PageSize) < _state.State.TotalResults)
                    {
                        _state.State.CurrentPage = pageNumber.Value;
                        stateChanged = true;
                    }
                    break;
                case "delete_result": // Action name updated from cancel_search
                    var senderGrain = _grainFactory.GetGrain<ITelegramMessageSenderGrain>(0);
                    string feedbackText = "搜索结果已删除。";
                    if (_state.State.SearchResultMessageId != 0)
                    {
                        try
                        {
                            await senderGrain.DeleteMessageAsync(_state.State.InitiatingChatId, _state.State.SearchResultMessageId);
                            _logger.Information("Grain {GrainId}: Deleted search result message {MessageId} in chat {ChatId}.",
                                this.GetPrimaryKeyString(), _state.State.SearchResultMessageId, _state.State.InitiatingChatId);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Grain {GrainId}: Failed to delete search result message {MessageId} in chat {ChatId}.",
                                this.GetPrimaryKeyString(), _state.State.SearchResultMessageId, _state.State.InitiatingChatId);
                            feedbackText = "删除搜索结果失败，但本次搜索会话已结束。";
                        }
                    }
                    else
                    {
                        _logger.Warning("Grain {GrainId}: delete_result action called, but SearchResultMessageId was 0. No message to delete.", this.GetPrimaryKeyString());
                        feedbackText = "没有可删除的搜索结果消息，搜索会话已结束。";
                    }
                    
                    // Send a confirmation to the user via a new message, as the original callback query message might be deleted or its keyboard gone.
                    // Or, use AnswerCallbackQueryAsync if appropriate (but that's for the button click itself, not a new message).
                    // For simplicity, sending a new message.
                    // await senderGrain.SendMessageAsync(new TelegramMessageToSend {
                    //     ChatId = _state.State.InitiatingChatId,
                    //     Text = feedbackText 
                    //     // ReplyToMessageId = _state.State.InitiatingMessageId // Replying to original command might be best here
                    // });

                    // It's often better to just answer the callback query for "delete" actions to avoid clutter.
                    // The message is deleted, further text feedback might be redundant unless an error occurred.
                    // For now, we'll rely on the message disappearing as feedback.

                    await _state.ClearStateAsync(); // Clear state
                    DeactivateOnIdle(); // Deactivate the grain as the session is over.
                    return; // Exit early
                default:
                    _logger.Warning("Grain {GrainId}: Unknown paging action '{Action}'", this.GetPrimaryKeyString(), action);
                    return; // Exit early
            }

            if (stateChanged)
            {
                await ExecuteSearchAndRespondAsync();
            }
            else
            {
                 _logger.Information("Grain {GrainId}: Paging action '{Action}' resulted in no state change or invalid page.", this.GetPrimaryKeyString(), action);
                // Optionally, send a message to the user if the action was invalid (e.g., already on first/last page)
                // For example, by sending an answerCallbackQuery to the bot.
            }
        }

        private async Task ExecuteSearchAndRespondAsync()
        {
            if (string.IsNullOrEmpty(_state.State.OriginalQuery))
            {
                _logger.Warning("Grain {GrainId}: ExecuteSearchAndRespondAsync called with no original query.", this.GetPrimaryKeyString());
                return;
            }

            var searchOption = new SearchOption
            {
                Search = _state.State.OriginalQuery,
                ChatId = _state.State.InitiatingChatId,
                IsGroup = _state.State.InitiatingChatId < 0, // Set IsGroup based on ChatId
                Skip = (_state.State.CurrentPage - 1) * _state.State.PageSize,
                Take = _state.State.PageSize,
                Count = _state.State.TotalResults // Pass current total, ISearchService might update it
            };

            _logger.Information("Grain {GrainId}: Executing search for query '{Query}', ChatId {ChatId}, IsGroup {IsGroup}, Page {Page}, PageSize {PageSize}",
                this.GetPrimaryKeyString(), searchOption.Search, searchOption.ChatId, searchOption.IsGroup, _state.State.CurrentPage, searchOption.Take);

            SearchOption resultOption;
            try
            {
                resultOption = await _searchService.Search(searchOption);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Grain {GrainId}: Error calling ISearchService.Search", this.GetPrimaryKeyString());
                var errorSender = _grainFactory.GetGrain<ITelegramMessageSenderGrain>(0);
                await errorSender.SendMessageAsync(new TelegramMessageToSend {
                    ChatId = _state.State.InitiatingChatId,
                    Text = "搜索时发生错误，请稍后再试。",
                    ReplyToMessageId = _state.State.InitiatingMessageId
                });
                return;
            }
            
            _state.State.TotalResults = resultOption.Count; 

            var sender = _grainFactory.GetGrain<ITelegramMessageSenderGrain>(0);
            StringBuilder responseTextBuilder = new StringBuilder();
            string grainId = this.GetPrimaryKeyString();

            if (resultOption.Messages == null || !resultOption.Messages.Any())
            {
                responseTextBuilder.AppendLine($"未找到关于 \"{_state.State.OriginalQuery}\" 的结果。");
            }
            else
            {
                int totalPages = (int)Math.Ceiling((double)_state.State.TotalResults / _state.State.PageSize);
                if (totalPages == 0 && _state.State.TotalResults > 0) totalPages = 1; // Handle case where PageSize > TotalResults

                responseTextBuilder.AppendLine($"搜索 \"{_state.State.OriginalQuery}\" 的结果 (第 {_state.State.CurrentPage}/{totalPages} 页 | 共 {_state.State.TotalResults} 条):");
                foreach (var msg in resultOption.Messages)
                {
                    string contentPreview = msg.Content != null ? msg.Content.Substring(0, Math.Min(msg.Content.Length, 70)) : "[无内容]";
                    if (msg.Content != null && msg.Content.Length > 70) contentPreview += "...";
                    
                    string senderInfo = $"用户ID: {msg.FromUserId}"; // Only UserId is available in Model.Data.Message
                    
                    responseTextBuilder.AppendLine($"---------------------\n消息ID: {msg.MessageId}\n内容: {contentPreview}\n发送者: {senderInfo}\n时间: {msg.DateTime:yyyy-MM-dd HH:mm}");
                }
            }
            
            var keyboard = BuildInlineKeyboard(grainId);
            string currentResponseText = responseTextBuilder.ToString();

            // 优先编辑原消息（只有首次或编辑失败时才发新消息）
            if (_state.State.SearchResultMessageId != 0)
            {
                var editSuccess = await sender.EditMessageTextAsync(
                    _state.State.InitiatingChatId,
                    _state.State.SearchResultMessageId,
                    currentResponseText,
                    keyboard
                );
                if (!editSuccess)
                {
                    // 编辑失败则发新消息
                    var sentMessageId = await sender.SendMessageAsync(new TelegramMessageToSend
                    {
                        ChatId = _state.State.InitiatingChatId,
                        Text = currentResponseText,
                        ReplyMarkup = keyboard
                    });
                    if (sentMessageId.HasValue)
                        _state.State.SearchResultMessageId = sentMessageId.Value;
                    else
                        _state.State.SearchResultMessageId = 0;
                }
            }
            else
            {
                var sentMessageId = await sender.SendMessageAsync(new TelegramMessageToSend
                {
                    ChatId = _state.State.InitiatingChatId,
                    Text = currentResponseText,
                    ReplyMarkup = keyboard
                });
                if (sentMessageId.HasValue)
                    _state.State.SearchResultMessageId = sentMessageId.Value;
                else
                    _state.State.SearchResultMessageId = 0;
            }
            await _state.WriteStateAsync();
        }

        private InlineKeyboardMarkup BuildInlineKeyboard(string grainId)
        {
            var buttonsRow = new List<InlineKeyboardButton>();
            int totalPages = (int)Math.Ceiling((double)_state.State.TotalResults / _state.State.PageSize);
            if (totalPages == 0 && _state.State.TotalResults > 0) totalPages = 1;


            if (_state.State.CurrentPage > 1)
            {
                buttonsRow.Add(InlineKeyboardButton.WithCallbackData("⬅️ 上一页", $"searchgrain:{grainId}:prev_page"));
            }

            // Simple page indicator within a button (not ideal, but works without complex logic for many page buttons)
            if (totalPages > 0)
            {
                 buttonsRow.Add(InlineKeyboardButton.WithCallbackData($"第 {_state.State.CurrentPage}/{totalPages} 页", $"searchgrain:{grainId}:noop")); // noop or current_page
            }

            if (_state.State.CurrentPage < totalPages)
            {
                buttonsRow.Add(InlineKeyboardButton.WithCallbackData("下一页", $"searchgrain:{grainId}:next_page")); // Text updated
            }
            
            var keyboardRows = new List<List<InlineKeyboardButton>>();
            if (buttonsRow.Any()) {
                keyboardRows.Add(buttonsRow);
            }

            // Add "Delete this result" button, always present if there are results to show a keyboard for.
            // This button will delete the current search result message and end the session.
            // Display this button if there are any results (TotalResults > 0) or if it's the first page of a potentially empty search (TotalResults == -1 but we are about to show something)
            // A simpler check: if we are building a keyboard with other buttons (prev/next/page), it implies there are results.
            // Or, more directly, if _state.State.SearchResultMessageId is set (meaning a result message was sent).
            // For now, let's show it if TotalResults indicates there could be a message.
            if (_state.State.TotalResults != 0) // Show if not explicitly zero results. -1 means first search, >0 means results exist.
            {
                 keyboardRows.Add(new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData("删除此结果", $"searchgrain:{grainId}:delete_result") }); // Text and action updated
            }
            
            return keyboardRows.Any() ? new InlineKeyboardMarkup(keyboardRows) : null;
        }
    }
}
