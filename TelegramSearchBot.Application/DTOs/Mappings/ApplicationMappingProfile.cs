using AutoMapper;
using TelegramSearchBot.Application.DTOs.Requests;
using TelegramSearchBot.Application.DTOs.Responses;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Application.DTOs.Mappings
{
    /// <summary>
    /// AutoMapper配置文件
    /// </summary>
    public class ApplicationMappingProfile : Profile
    {
        public ApplicationMappingProfile()
        {
            // Message相关映射
            CreateMap<TelegramSearchBot.Model.Data.Message, MessageDto>()
                .ForMember(dest => dest.Extensions, opt => opt.Ignore()); // 简化实现：暂时忽略扩展数据

            CreateMap<TelegramSearchBot.Model.Data.Message, MessageResponseDto>()
                .ForMember(dest => dest.FromUser, opt => opt.MapFrom(src => new UserInfoDto { Id = src.FromUserId }))
                .ForMember(dest => dest.Extensions, opt => opt.Ignore()); // 简化实现：暂时忽略扩展数据

            CreateMap<MessageDto, TelegramSearchBot.Model.Data.Message>()
                .ForMember(dest => dest.Id, opt => opt.Ignore()) // ID由数据库生成
                .ForMember(dest => dest.MessageExtensions, opt => opt.Ignore()); // 简化实现：暂时忽略扩展数据

            // 简化实现：其他映射关系可以根据需要添加
        }
    }
}