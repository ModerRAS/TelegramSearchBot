using System.IO;
using System.Threading.Tasks;
using TelegramSearchBot.Common.Model.DO;

namespace TelegramSearchBot.Interface.AI.OCR
{
    public interface IPaddleOCRService
    {
        Task<string> ExecuteAsync(Stream file);
        
        /// <summary>
        /// 执行OCR识别，返回包含文字和坐标信息的完整结果
        /// </summary>
        /// <param name="file">图片文件流</param>
        /// <returns>包含文字、坐标和置信度的OCR结果</returns>
        Task<PaddleOCRResult> ExecuteWithCoordinatesAsync(Stream file);
    }
}
