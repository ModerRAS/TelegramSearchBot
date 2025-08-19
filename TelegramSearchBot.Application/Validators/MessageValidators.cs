using FluentValidation;
using TelegramSearchBot.Application.DTOs.Requests;
using TelegramSearchBot.Application.Features.Messages;

namespace TelegramSearchBot.Application.Validators
{
    /// <summary>
    /// CreateMessageCommand验证器
    /// </summary>
    public class CreateMessageCommandValidator : AbstractValidator<CreateMessageCommand>
    {
        public CreateMessageCommandValidator()
        {
            RuleFor(x => x.MessageDto)
                .NotNull()
                .WithMessage("Message data cannot be null");

            RuleFor(x => x.GroupId)
                .GreaterThan(0)
                .WithMessage("GroupId must be greater than 0");

            When(x => x.MessageDto != null, () =>
            {
                RuleFor(x => x.MessageDto.MessageId)
                    .GreaterThan(0)
                    .WithMessage("MessageId must be greater than 0");

                RuleFor(x => x.MessageDto.FromUserId)
                    .GreaterThan(0)
                    .WithMessage("FromUserId must be greater than 0");

                RuleFor(x => x.MessageDto.Content)
                    .NotEmpty()
                    .WithMessage("Content cannot be empty");
            });
        }
    }

    /// <summary>
    /// UpdateMessageCommand验证器
    /// </summary>
    public class UpdateMessageCommandValidator : AbstractValidator<UpdateMessageCommand>
    {
        public UpdateMessageCommandValidator()
        {
            RuleFor(x => x.Id)
                .GreaterThan(0)
                .WithMessage("Id must be greater than 0");

            RuleFor(x => x.MessageDto)
                .NotNull()
                .WithMessage("Message data cannot be null");

            When(x => x.MessageDto != null, () =>
            {
                RuleFor(x => x.MessageDto.Content)
                    .NotEmpty()
                    .WithMessage("Content cannot be empty");
            });
        }
    }

    /// <summary>
    /// DeleteMessageCommand验证器
    /// </summary>
    public class DeleteMessageCommandValidator : AbstractValidator<DeleteMessageCommand>
    {
        public DeleteMessageCommandValidator()
        {
            RuleFor(x => x.Id)
                .GreaterThan(0)
                .WithMessage("Id must be greater than 0");
        }
    }

    /// <summary>
    /// GetMessageByIdQuery验证器
    /// </summary>
    public class GetMessageByIdQueryValidator : AbstractValidator<GetMessageByIdQuery>
    {
        public GetMessageByIdQueryValidator()
        {
            RuleFor(x => x.Id)
                .GreaterThan(0)
                .WithMessage("Id must be greater than 0");
        }
    }

    /// <summary>
    /// SearchMessagesQuery验证器
    /// </summary>
    public class SearchMessagesQueryValidator : AbstractValidator<SearchMessagesQuery>
    {
        public SearchMessagesQueryValidator()
        {
            RuleFor(x => x.Query)
                .NotEmpty()
                .WithMessage("Search query cannot be empty");

            RuleFor(x => x.Take)
                .GreaterThan(0)
                .LessThanOrEqualTo(100)
                .WithMessage("Take must be between 1 and 100");

            RuleFor(x => x.Skip)
                .GreaterThanOrEqualTo(0)
                .WithMessage("Skip must be greater than or equal to 0");
        }
    }
}