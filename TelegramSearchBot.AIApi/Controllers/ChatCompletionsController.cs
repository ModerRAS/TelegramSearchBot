using Microsoft.AspNetCore.Mvc;
using TelegramSearchBot.AIApi.Interface;
using TelegramSearchBot.AIApi.Model.ChatModel;

namespace TelegramSearchBot.AIApi.Controllers
{
    [ApiController]
    [Route("v1/chat/completions")]
    public class ChatCompletionsController : ControllerBase {
        private readonly IChatService _chatService;

        public ChatCompletionsController(IChatService chatService) {
            _chatService = chatService;
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] ChatRequest request) {
            if (request.stream) {
                Response.Headers.Add("Content-Type", "text/event-stream");
                await foreach (var chunk in _chatService.StreamChatAsync(request, HttpContext.RequestAborted)) {
                    await Response.WriteAsync($"data: {chunk}\n\n");
                    await Response.Body.FlushAsync();
                }
                return new EmptyResult();
            } else {
                var response = await _chatService.GetChatAsync(request);
                return Ok(response);
            }
        }
    }
}
