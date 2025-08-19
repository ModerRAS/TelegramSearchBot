using MediatR;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Model.Notifications
{
    /// <summary>
    /// 消息向量生成通知
    /// 用于通知向量服务生成消息向量
    /// </summary>
    public class MessageVectorGenerationNotification : INotification
    {
        public Message Message { get; }

        public MessageVectorGenerationNotification(Message message)
        {
            Message = message;
        }
    }
}