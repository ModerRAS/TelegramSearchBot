using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using ZXing.SkiaSharp;

namespace TelegramSearchBot.Service {
    public class AutoQRService {
        /// <summary>
        /// 按理说是进来文件出去字符的
        /// </summary>
        /// <param name="messageOption"></param>
        /// <returns></returns>
        public async Task<string> ExecuteAsync(MemoryStream file) {

            using (var inputStream = new SKManagedStream(file))
            using (var original = SKBitmap.Decode(inputStream)) {

                // create a barcode reader instance
                var reader = new BarcodeReader();
                // detect and decode the barcode inside the bitmap
                var result = reader.Decode(original);
                // do something with the result
                if (result != null) {
                    var txtDecoderType = result.BarcodeFormat.ToString();
                    var txtDecoderContent = result.Text;
                    return result.Text;
                } else {
                    return string.Empty;
                }
            }

            
        }
    }
}
