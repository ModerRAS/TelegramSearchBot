﻿using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using TelegramSearchBot.Interface.Controller;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Executor {
    public class ControllerExecutor {
        private readonly IEnumerable<IOnUpdate> _controllers;

        public ControllerExecutor(IEnumerable<IOnUpdate> controllers) {
            _controllers = controllers;
        }

        public async Task ExecuteControllers(Telegram.Bot.Types.Update e) {
            var executed = new HashSet<Type>();
            var pending = new List<IOnUpdate>(_controllers);
            var PipelineContext = new PipelineContext() { Update = e, PipelineCache = new Dictionary<string, dynamic>() };
            while (pending.Count > 0) {
                var controller = pending.FirstOrDefault(c => !c.Dependencies.Any(d => !executed.Contains(d)));

                if (controller != null) {
                    try {
                        await controller.ExecuteAsync(PipelineContext);
                    } catch (Exception ex) {
                        //Log.Error(ex, $"Message Pre Process Error: {e.Message.Chat.FirstName} {e.Message.Chat.LastName} {e.Message.Chat.Title} {e.Message.Chat.Id}/{e.Message.MessageId}");
                    }
                    executed.Add(controller.GetType());
                    pending.Remove(controller);
                } else {
                    throw new InvalidOperationException("Circular dependency detected or unmet dependencies.");
                }
            }
        }
    }
}
