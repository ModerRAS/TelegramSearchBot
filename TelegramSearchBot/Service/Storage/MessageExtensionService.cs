using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.Storage {
    public class MessageExtensionService : IMessageExtensionService, IService {
        private readonly DataDbContext _context;
        public string ServiceName => "MessageExtensionService";

        public MessageExtensionService(DataDbContext context) {
            _context = context;
        }

        public async Task<MessageExtension> GetByIdAsync(int id) {
            return await _context.MessageExtensions.FindAsync(id);
        }

        public async Task<List<MessageExtension>> GetByMessageDataIdAsync(long messageDataId) {
            return await _context.MessageExtensions
                .Where(x => x.MessageDataId == messageDataId)
                .ToListAsync();
        }

        public async Task AddOrUpdateAsync(MessageExtension extension) {
            var existing = await _context.MessageExtensions
                .FirstOrDefaultAsync(x => x.MessageDataId == extension.MessageDataId && x.Name == extension.Name);
            
            if (existing != null) {
                existing.Value = extension.Value;
                _context.MessageExtensions.Update(existing);
            } else {
                await _context.MessageExtensions.AddAsync(extension);
            }

            await _context.SaveChangesAsync();
        }

        public async Task AddOrUpdateAsync(long messageDataId, string name, string value) {
            var existing = await _context.MessageExtensions
                .FirstOrDefaultAsync(x => x.MessageDataId == messageDataId && x.Name == name);
            
            if (existing != null) {
                existing.Value = value;
                _context.MessageExtensions.Update(existing);
            } else {
                await _context.MessageExtensions.AddAsync(new MessageExtension {
                    MessageDataId = messageDataId,
                    Name = name,
                    Value = value
                });
            }

            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id) {
            var extension = await GetByIdAsync(id);
            if (extension != null) {
                _context.MessageExtensions.Remove(extension);
                await _context.SaveChangesAsync();
            }
        }

        public async Task DeleteByMessageDataIdAsync(long messageDataId) {
            var extensions = await GetByMessageDataIdAsync(messageDataId);
            _context.MessageExtensions.RemoveRange(extensions);
            await _context.SaveChangesAsync();
        }
    }
}
