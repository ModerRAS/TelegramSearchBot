using AutoMapper;
using TelegramSearchBot.Domain.Message;
using TelegramSearchBot.Domain.Message.ValueObjects;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Application.Mappings
{
    /// <summary>
    /// Message对象映射配置
    /// </summary>
    public class MessageMappingProfile : Profile
    {
        public MessageMappingProfile()
        {
            // MessageAggregate 到 Message 的映射
            CreateMap<MessageAggregate, Message>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id.TelegramMessageId))
                .ForMember(dest => dest.GroupId, opt => opt.MapFrom(src => src.Id.ChatId))
                .ForMember(dest => dest.MessageId, opt => opt.MapFrom(src => src.Id.TelegramMessageId))
                .ForMember(dest => dest.FromUserId, opt => opt.MapFrom(src => src.Metadata.FromUserId))
                .ForMember(dest => dest.ReplyToUserId, opt => opt.MapFrom(src => src.Metadata.ReplyToUserId))
                .ForMember(dest => dest.ReplyToMessageId, opt => opt.MapFrom(src => src.Metadata.ReplyToMessageId))
                .ForMember(dest => dest.Content, opt => opt.MapFrom(src => src.Content.Value))
                .ForMember(dest => dest.DateTime, opt => opt.MapFrom(src => src.Metadata.Timestamp));

            // Message 到 MessageAggregate 的映射
            CreateMap<Message, MessageAggregate>()
                .ConstructUsing(src => MessageAggregate.Create(
                    src.GroupId,
                    src.MessageId,
                    src.Content,
                    src.FromUserId,
                    src.ReplyToUserId,
                    src.ReplyToMessageId,
                    src.DateTime));

            // MessageOption 到 MessageAggregate 的映射
            CreateMap<MessageOption, MessageAggregate>()
                .ConstructUsing(src => MessageAggregate.Create(
                    src.ChatId,
                    src.MessageId,
                    src.Content,
                    src.UserId,
                    src.ReplyTo,
                    src.ReplyTo,
                    src.DateTime));

            // 其他相关映射...
            CreateMap<MessageExtension, MessageExtension>();
            CreateMap<MessageContent, string>()
                .ConvertUsing(src => src.Value);
            
            CreateMap<string, MessageContent>()
                .ConstructUsing(src => new MessageContent(src));
        }
    }
}