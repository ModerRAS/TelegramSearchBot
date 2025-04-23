namespace TelegramSearchBot.AIApi.Interface {
    public interface ICompletionProxy {
        Task ForwardCompletionRequestAsync(HttpContext context);
    }
}
