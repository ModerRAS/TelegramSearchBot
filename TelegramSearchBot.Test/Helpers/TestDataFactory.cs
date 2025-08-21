using System;
using System.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Common.Model;
using MessageEntity = TelegramSearchBot.Model.Data.Message;

namespace TelegramSearchBot.Test.Helpers
{
    /// <summary>
    /// 测试数据工厂，用于创建各种类型的测试数据
    /// </summary>
    public static class TestDataFactory
    {
        private static readonly Random _random = new Random();

        #region Message Creation

        /// <summary>
        /// 创建有效的MessageOption对象（兼容MessageTestDataFactory）
        /// </summary>
        public static TelegramSearchBot.Model.MessageOption CreateValidMessageOption(
            long userId = 1L,
            long chatId = 100L,
            long messageId = 1000L,
            string content = "Test message",
            long replyTo = 0L)
        {
            return new TelegramSearchBot.Model.MessageOption
            {
                UserId = userId,
                User = new User
                {
                    Id = userId,
                    FirstName = "Test",
                    LastName = "User",
                    Username = "testuser",
                    IsBot = false,
                    IsPremium = false
                },
                ChatId = chatId,
                Chat = new Chat
                {
                    Id = chatId,
                    Title = "Test Chat",
                    Type = ChatType.Group,
                    IsForum = false
                },
                MessageId = messageId,
                Content = content,
                DateTime = DateTime.UtcNow,
                ReplyTo = replyTo
            };
        }

        /// <summary>
        /// 创建长消息（超过4000字符）
        /// </summary>
        public static TelegramSearchBot.Model.MessageOption CreateLongMessage(int targetLength = 5000)
        {
            var content = new string('a', targetLength);
            return CreateValidMessageOption(content: content);
        }

        /// <summary>
        /// 创建带有回复消息的MessageOption
        /// </summary>
        public static TelegramSearchBot.Model.MessageOption CreateMessageWithReply(
            long replyToMessageId = 1000L,
            long replyToUserId = 1L)
        {
            return CreateValidMessageOption(
                content: "This is a reply message",
                replyTo: replyToMessageId);
        }

        /// <summary>
        /// 创建包含特殊字符的MessageOption
        /// </summary>
        public static TelegramSearchBot.Model.MessageOption CreateMessageWithSpecialCharacters(
            bool includeChinese = true,
            bool includeEmoji = true,
            bool includeSpecialChars = true)
        {
            var content = "Test message";
            
            if (includeChinese)
            {
                content += " 中文测试";
            }
            
            if (includeEmoji)
            {
                content += " 😊🎉";
            }
            
            if (includeSpecialChars)
            {
                content += " @#$%^&*()";
            }
            
            return CreateValidMessageOption(content: content);
        }

        /// <summary>
        /// 创建基础文本消息
        /// </summary>
        public static TelegramSearchBot.Model.Data.Message CreateTextMessage(int messageId = 1, string text = "测试消息")
        {
            return new TelegramSearchBot.Model.Data.Message
            {
                MessageId = messageId,
                GroupId = -100123456789,
                FromUserId = 123456789,
                Content = text,
                DateTime = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 创建图片消息
        /// </summary>
        public static TelegramSearchBot.Model.Data.Message CreatePhotoMessage(int messageId = 1)
        {
            return new TelegramSearchBot.Model.Data.Message
            {
                MessageId = messageId,
                GroupId = -100123456789,
                FromUserId = 123456789,
                Content = "这是一条图片消息",
                DateTime = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 创建语音消息
        /// </summary>
        public static MessageEntity CreateVoiceMessage(int messageId = 1)
        {
            return new MessageEntity
            {
                MessageId = messageId,
                GroupId = -100123456789,
                FromUserId = 123456789,
                Content = "这是一条语音消息",
                DateTime = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 创建视频消息
        /// </summary>
        public static MessageEntity CreateVideoMessage(int messageId = 1)
        {
            return new MessageEntity
            {
                MessageId = messageId,
                GroupId = -100123456789,
                FromUserId = 123456789,
                Content = "这是一条视频消息",
                DateTime = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 创建有效的消息（兼容MessageTestDataFactory）
        /// </summary>
        public static MessageEntity CreateValidMessage(
            long groupId = 100L,
            long messageId = 1000L,
            long fromUserId = 1L,
            string content = "Test message")
        {
            return new MessageEntity
            {
                GroupId = groupId,
                MessageId = messageId,
                FromUserId = fromUserId,
                Content = content,
                DateTime = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 创建多媒体消息（文本+图片）
        /// </summary>
        public static MessageEntity CreateMultimediaMessage(int messageId = 1, string text = "多媒体测试消息")
        {
            return new MessageEntity
            {
                MessageId = messageId,
                GroupId = -100123456789,
                FromUserId = 123456789,
                Content = text,
                DateTime = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 创建已处理的消息
        /// </summary>
        public static MessageEntity CreateProcessedMessage(int messageId = 1, string text = "已处理的消息")
        {
            return new MessageEntity
            {
                MessageId = messageId,
                GroupId = -100123456789,
                FromUserId = 123456789,
                Content = text,
                DateTime = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 创建编辑过的消息
        /// </summary>
        public static MessageEntity CreateEditedMessage(int messageId = 1, string originalText = "原始消息", string editedText = "编辑后的消息")
        {
            return new MessageEntity
            {
                MessageId = messageId,
                GroupId = -100123456789,
                FromUserId = 123456789,
                Content = editedText,
                DateTime = DateTime.UtcNow
            };
        }

        #endregion

        #region Message Bulk Creation

        /// <summary>
        /// 创建消息列表
        /// </summary>
        public static List<MessageEntity> CreateMessageList(int count, MessageType type = MessageType.Text)
        {
            var messages = new List<MessageEntity>();

            for (int i = 1; i <= count; i++)
            {
                MessageEntity message = type switch
                {
                    MessageType.Text => CreateTextMessage(i, $"测试消息 {i}"),
                    MessageType.Photo => CreatePhotoMessage(i),
                    MessageType.Voice => CreateVoiceMessage(i),
                    MessageType.Video => CreateVideoMessage(i),
                    MessageType.Multimedia => CreateMultimediaMessage(i, $"多媒体消息 {i}"),
                    _ => CreateTextMessage(i, $"测试消息 {i}")
                };

                messages.Add(message);
            }

            return messages;
        }

        /// <summary>
        ///创建包含搜索关键词的消息列表
        /// </summary>
        public static List<MessageEntity> CreateSearchableMessageList()
        {
            return new List<MessageEntity>
            {
                CreateTextMessage(1, "这是一个关于人工智能的测试消息"),
                CreateTextMessage(2, "机器学习和深度学习是AI的重要分支"),
                CreateTextMessage(3, "自然语言处理在AI领域应用广泛"),
                CreateTextMessage(4, "计算机视觉技术发展迅速"),
                CreateTextMessage(5, "大数据分析是现代AI的基础"),
                CreateTextMessage(6, "云计算为AI提供了强大的计算能力"),
                CreateTextMessage(7, "物联网与AI的结合产生了新的应用场景"),
                CreateTextMessage(8, "区块链技术在AI数据安全中的应用"),
                CreateTextMessage(9, "量子计算将推动AI技术的突破"),
                CreateTextMessage(10, "AI伦理和安全性是重要议题")
            };
        }

        /// <summary>
        /// 创建AI处理测试用的消息列表
        /// </summary>
        public static List<MessageEntity> CreateAIProcessingMessageList()
        {
            return new List<MessageEntity>
            {
                CreatePhotoMessage(1001), // 纯图片消息，需要OCR
                CreateVoiceMessage(1002), // 纯语音消息，需要ASR
                CreateVideoMessage(1003), // 纯视频消息，需要视频ASR
                CreateMultimediaMessage(1004, "请分析这张图片的内容"), // 图片+文字，需要OCR+LLM
                CreateMultimediaMessage(1005, "这段语音说了什么？"), // 语音+文字，需要ASR+LLM
                CreateTextMessage(1006, "请总结以下内容：这是一段很长的文本内容..."), // 纯文字，需要LLM分析
                CreateTextMessage(1007, "翻译这句话：Hello, how are you?"), // 纯文字，需要翻译
                CreateTextMessage(1008, "这段话的情感是什么？我很开心今天能够完成这个项目！") // 纯文字，需要情感分析
            };
        }

        #endregion

        #region Message Extensions

        /// <summary>
        /// 创建消息扩展
        /// </summary>
        public static MessageExtension CreateMessageExtension(long messageId, string extensionType, string extensionData)
        {
            return new MessageExtension
            {
                MessageDataId = messageId,
                ExtensionType = extensionType,
                ExtensionData = extensionData
            };
        }

        /// <summary>
        /// 创建OCR扩展
        /// </summary>
        public static MessageExtension CreateOCRExtension(long messageId, string ocrText = "图片识别的文字")
        {
            return CreateMessageExtension(messageId, "ocr", JsonSerializer.Serialize(new { text = ocrText, confidence = 0.95 }));
        }

        /// <summary>
        /// 创建ASR扩展
        /// </summary>
        public static MessageExtension CreateASRExtension(long messageId, string asrText = "语音转写的文字")
        {
            return CreateMessageExtension(messageId, "asr", JsonSerializer.Serialize(new { text = asrText, duration = 5.2 }));
        }

        /// <summary>
        /// 创建LLM扩展
        /// </summary>
        public static MessageExtension CreateLLMExtension(long messageId, string llmResponse = "AI分析结果")
        {
            return CreateMessageExtension(messageId, "llm", JsonSerializer.Serialize(new { response = llmResponse, model = "gpt-3.5-turbo" }));
        }

        /// <summary>
        /// 创建向量扩展
        /// </summary>
        public static MessageExtension CreateVectorExtension(long messageId, float[]? vector = null)
        {
            var vec = vector ?? GenerateRandomVector(768); // 768维向量
            return CreateMessageExtension(messageId, "vector", JsonSerializer.Serialize(new { dimensions = vec.Length, data = vec }));
        }

        #endregion

        #region Telegram Bot Types

        /// <summary>
        /// 创建Telegram Bot消息
        /// </summary>
        public static Telegram.Bot.Types.Message CreateTelegramBotMessage(int messageId, string text, long chatId = -100123456789)
        {
            return new Telegram.Bot.Types.Message
            {
                MessageId = messageId,
                Chat = new Chat { Id = chatId },
                Text = text,
                From = new User { Id = 123456789, FirstName = "Test", LastName = "User" },
                Date = DateTime.UtcNow,
                MessageThreadId = 1
            };
        }

        /// <summary>
        /// 创建Telegram Bot图片消息
        /// </summary>
        public static Telegram.Bot.Types.Message CreateTelegramBotPhotoMessage(int messageId, long chatId = -100123456789)
        {
            return new Telegram.Bot.Types.Message
            {
                MessageId = messageId,
                Chat = new Chat { Id = chatId },
                Photo = new[] { new PhotoSize { FileId = "test_photo_1", Width = 1280, Height = 720 } },
                From = new User { Id = 123456789, FirstName = "Test", LastName = "User" },
                Date = DateTime.UtcNow,
                MessageThreadId = 1
            };
        }

        /// <summary>
        /// 创建Telegram Bot更新
        /// </summary>
        public static Telegram.Bot.Types.Update CreateTelegramBotUpdate(int updateId, Telegram.Bot.Types.Message message)
        {
            return new Telegram.Bot.Types.Update
            {
                Id = updateId,
                Message = message
            };
        }

        /// <summary>
        /// 创建Telegram Bot回调查询
        /// </summary>
        public static CallbackQuery CreateCallbackQuery(string callbackQueryId, Telegram.Bot.Types.Message message, string data)
        {
            return new CallbackQuery
            {
                Id = callbackQueryId,
                Message = message,
                From = new User { Id = 123456789, FirstName = "Test", LastName = "User" },
                Data = data
            };
        }

        /// <summary>
        /// 创建内联键盘
        /// </summary>
        public static InlineKeyboardMarkup CreateInlineKeyboardMarkup(params string[][] buttons)
        {
            var keyboardButtons = buttons.Select(row => 
                row.Select(text => new InlineKeyboardButton(text)).ToArray()).ToArray();
            
            return new InlineKeyboardMarkup(keyboardButtons);
        }

        #endregion

        #region Test Data Sets

        /// <summary>
        /// 获取完整的测试数据集
        /// </summary>
        public static (List<MessageEntity> Messages, List<MessageExtension> Extensions) GetFullTestData()
        {
            var messages = new List<MessageEntity>();
            var extensions = new List<MessageExtension>();

            // 添加基础文本消息
            messages.AddRange(CreateSearchableMessageList());

            // 添加AI处理消息
            messages.AddRange(CreateAIProcessingMessageList());

            // 添加消息扩展
            extensions.Add(CreateOCRExtension(1001, "这是一张测试图片"));
            extensions.Add(CreateASRExtension(1002, "这是一段测试语音"));
            extensions.Add(CreateLLMExtension(1006, "这段话讨论了人工智能的发展"));
            extensions.Add(CreateVectorExtension(1));

            return (messages, extensions);
        }

        /// <summary>
        /// 获取性能测试数据集
        /// </summary>
        public static List<MessageEntity> GetPerformanceTestData(int count = 1000)
        {
            var messages = new List<MessageEntity>();
            var keywords = new[] { "测试", "消息", "数据", "搜索", "AI", "机器学习", "深度学习", "自然语言处理" };

            for (int i = 1; i <= count; i++)
            {
                var randomKeyword = keywords[_random.Next(keywords.Length)];
                var text = $"性能测试消息 {i}，包含关键词：{randomKeyword}，随机数：{_random.Next(1, 10000)}";
                
                messages.Add(new MessageEntity
                {
                    MessageId = i,
                    GroupId = -100123456789,
                    FromUserId = _random.Next(100000000, 999999999),
                    Content = text,
                    DateTime = DateTime.UtcNow.AddMinutes(-_random.Next(0, 1440))
                });
            }

            return messages;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// 生成随机字节数组
        /// </summary>
        private static byte[] GenerateRandomBytes(int size)
        {
            var bytes = new byte[size];
            _random.NextBytes(bytes);
            return bytes;
        }

        /// <summary>
        /// 生成随机向量
        /// </summary>
        private static float[] GenerateRandomVector(int dimensions)
        {
            var vector = new float[dimensions];
            for (int i = 0; i < dimensions; i++)
            {
                vector[i] = (float)_random.NextDouble();
            }
            return vector;
        }

        /// <summary>
        /// 生成随机用户ID
        /// </summary>
        public static long GenerateRandomUserId()
        {
            return _random.Next(100000000, 999999999);
        }

        /// <summary>
        /// 生成随机聊天ID
        /// </summary>
        public static long GenerateRandomChatId()
        {
            return -1000000000 - _random.Next(0, 1000000000);
        }

        /// <summary>
        /// 生成随机时间戳
        /// </summary>
        public static DateTime GenerateRandomTimestamp()
        {
            var daysAgo = _random.Next(0, 365);
            return DateTime.UtcNow.AddDays(-daysAgo);
        }

        #endregion
    }

    /// <summary>
    /// 消息类型枚举
    /// </summary>
    public enum MessageType
    {
        Text,
        Photo,
        Voice,
        Video,
        Multimedia
    }
}