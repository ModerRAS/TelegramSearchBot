using Serilog;
using Serilog.Core;
using Serilog.Events;
using Newtonsoft.Json;
using System;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

namespace TelegramSearchBot.Sink {
    public static class MemoryMappedViewAccessorExtensions {
        // 扩展方法：将指定的字节值填充到共享内存区域
        public static void Fill(this MemoryMappedViewAccessor accessor, byte value) {
            long length = accessor.Capacity;
            byte[] buffer = new byte[length];
            for (int i = 0; i < length; i++) {
                buffer[i] = value;
            }
            accessor.WriteArray(0, buffer, 0, buffer.Length);
        }
    }
    public class SharedMemoryLogSink : ILogEventSink {
        private const string MemoryMapName = "SharedLog";
        private const string WriteEventName = "LogWriteEvent";
        private const string ReadEventName = "LogReadEvent";
        private const int MemorySize = 4096; // 分配足够大的内存空间，保证能存放整个 JSON 字符串
        private readonly EventWaitHandle _writeEvent;
        private readonly EventWaitHandle _readEvent;

        public SharedMemoryLogSink() {
            // 创建或打开事件
            _writeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WriteEventName);
            _readEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ReadEventName);
        }

        public void Emit(LogEvent logEvent) {
            // 将日志事件转换为 JSON 字符串
            var logData = new {
                Timestamp = logEvent.Timestamp,
                Level = logEvent.Level.ToString(),
                Message = logEvent.RenderMessage(),
                Exception = logEvent.Exception?.ToString()
            };

            string jsonLog = JsonConvert.SerializeObject(logData);
            byte[] jsonLogBytes = Encoding.UTF8.GetBytes(jsonLog);
            int logLength = jsonLogBytes.Length;

            // 写入共享内存
            using (var mmf = MemoryMappedFile.CreateOrOpen(MemoryMapName, MemorySize)) {
                using (var accessor = mmf.CreateViewAccessor()) {
                    // 清空内存区域
                    accessor.Fill(0);

                    // 将 JSON 字符串写入共享内存
                    accessor.WriteArray(0, jsonLogBytes, 0, logLength);

                    // 通知消费者日志已写入
                    _writeEvent.Set();

                    // 等待消费者处理完日志
                    _readEvent.WaitOne(); // 这里会等到消费者告诉生产者可以继续写入
                }
            }
        }
    }
}
