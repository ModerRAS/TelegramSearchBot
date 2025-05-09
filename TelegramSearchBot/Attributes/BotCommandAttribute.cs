using System;

namespace TelegramSearchBot.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public class BotCommandAttribute : Attribute
    {
        public string Command { get; }
        public string Description { get; }

        public BotCommandAttribute(string command, string description)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                throw new ArgumentException("Command cannot be null or whitespace.", nameof(command));
            }
            if (command.StartsWith("/"))
            {
                Command = command.Substring(1); // Store without leading slash
            }
            else
            {
                Command = command;
            }
            Description = description ?? string.Empty;
        }
    }
}
