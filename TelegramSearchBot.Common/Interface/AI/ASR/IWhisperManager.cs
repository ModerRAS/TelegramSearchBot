using System.IO;
using System.Threading.Tasks;

namespace TelegramSearchBot.Interface.AI.ASR
{
    /// <summary>
    /// Whisper语音识别管理器接口
    /// </summary>
    public interface IWhisperManager
    {
        /// <summary>
        /// 执行语音识别
        /// </summary>
        /// <param name="wavStream">音频流</param>
        /// <returns>识别结果文本</returns>
        Task<string> ExecuteAsync(Stream wavStream);
    }
}