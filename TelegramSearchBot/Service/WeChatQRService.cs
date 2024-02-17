using Microsoft.Extensions.Logging;
using OpenCvSharp;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Controller;
using TelegramSearchBot.Intrerface;
using ZXing.SkiaSharp;

namespace TelegramSearchBot.Service {
    public class WeChatQRService : IStreamService, IService {
        public string ServiceName => "WeChatQRService";
        private readonly ILogger<WeChatQRService> logger;
        public WeChatQRCode opencvDecoder { get; set; }
        const string _wechat_QCODE_detector_prototxt_path = "Resources/wechat_qrcode/detect.prototxt";
        const string _wechat_QCODE_detector_caffe_model_path = "Resources/wechat_qrcode/detect.caffemodel";
        const string _wechat_QCODE_super_resolution_prototxt_path = "Resources/wechat_qrcode/sr.prototxt";
        const string _wechat_QCODE_super_resolution_caffe_model_path = "Resources/wechat_qrcode/sr.caffemodel";

        public WeChatQRService(ILogger<WeChatQRService> logger) {
            opencvDecoder = WeChatQRCode.Create(
                _wechat_QCODE_detector_prototxt_path,
                _wechat_QCODE_detector_caffe_model_path, 
                _wechat_QCODE_super_resolution_prototxt_path, 
                _wechat_QCODE_super_resolution_caffe_model_path
                );
            this.logger = logger;
        }
        public string DecodeByOpenCV(Bitmap img) {
            if (img == null) {
                return string.Empty;
            }

            Mat[] rects;
            string[] texts;
            try {
                Mat mat = OpenCvSharp.Extensions.BitmapConverter.ToMat(img);
                opencvDecoder.DetectAndDecode(mat, out rects, out texts);
                mat.Dispose();
            } catch (ZXing.ReaderException ex) {
                logger.LogWarning(ex, "OpenCV decode failed");
                return string.Empty;
            }

            if (rects.Length != 0) {
                return string.Join('\n', texts);
            } else
                return string.Empty;
        }

        /// <summary>
        /// 按理说是进来文件出去字符的
        /// </summary>
        /// <param name="messageOption"></param>
        /// <returns></returns>
#pragma warning disable CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
        public async Task<string> ExecuteAsync(Stream file) {
            // 先把文件转成bitmap
            Bitmap bitmap = new Bitmap(file);
            // 然后用opencv来解析
            string result = DecodeByOpenCV(bitmap);
            return result;
        }
#pragma warning restore CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
    }
}
