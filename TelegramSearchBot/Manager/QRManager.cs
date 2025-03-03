using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Service;

namespace TelegramSearchBot.Manager {
    public class QRManager {
        private readonly ILogger<QRManager> logger;
        public WeChatQRCode opencvDecoder { get; set; }
        public string _wechat_QCODE_detector_prototxt_path = $"{Env.WorkDir}/wechat_qrcode/detect.prototxt";
        public string _wechat_QCODE_detector_caffe_model_path = $"{Env.WorkDir}/wechat_qrcode/detect.caffemodel";
        public string _wechat_QCODE_super_resolution_prototxt_path = $"{Env.WorkDir}/wechat_qrcode/sr.prototxt";
        public string _wechat_QCODE_super_resolution_caffe_model_path = $"{Env.WorkDir}/wechat_qrcode/sr.caffemodel";

        public string detect_caffe_model_url = "https://github.com/WeChatCV/opencv_3rdparty/raw/wechat_qrcode/detect.caffemodel";
        public string detect_prototxt_url = "https://github.com/WeChatCV/opencv_3rdparty/raw/wechat_qrcode/detect.prototxt";
        public string sr_caffe_model_url = "https://github.com/WeChatCV/opencv_3rdparty/raw/wechat_qrcode/sr.caffemodel";
        public string sr_prototxt_url = "https://github.com/WeChatCV/opencv_3rdparty/raw/wechat_qrcode/sr.prototxt";

        public QRManager(ILogger<QRManager> logger) {
            if (!Directory.Exists(Path.Combine(Env.WorkDir, "wechat_qrcode"))) {
                Directory.CreateDirectory(Path.Combine(Env.WorkDir, "wechat_qrcode"));
            }
            var client = new WebClient();
            if (!File.Exists(_wechat_QCODE_detector_prototxt_path)) {
                client.DownloadFile(detect_prototxt_url, _wechat_QCODE_detector_prototxt_path);
            }
            if (!File.Exists(_wechat_QCODE_detector_caffe_model_path)) {
                client.DownloadFile(detect_caffe_model_url, _wechat_QCODE_detector_caffe_model_path);
            }
            if (!File.Exists(_wechat_QCODE_super_resolution_prototxt_path)) {
                client.DownloadFile(sr_prototxt_url, _wechat_QCODE_super_resolution_prototxt_path);
            }
            if (!File.Exists(_wechat_QCODE_super_resolution_caffe_model_path)) {
                client.DownloadFile(sr_caffe_model_url, _wechat_QCODE_super_resolution_caffe_model_path);
            }

            opencvDecoder = WeChatQRCode.Create(
            _wechat_QCODE_detector_prototxt_path,
            _wechat_QCODE_detector_caffe_model_path,
            _wechat_QCODE_super_resolution_prototxt_path,
            _wechat_QCODE_super_resolution_caffe_model_path
            );
            this.logger = logger;
        }
        public string DecodeByOpenCV(Mat img) {

            if (img == null) {
                return string.Empty;
            }

            Mat[] rects;
            string[] texts;
            try {
                opencvDecoder.DetectAndDecode(img, out rects, out texts);
                img.Dispose();
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
        public async Task<string> ExecuteAsync(string filePath) {
            using (var src = Cv2.ImRead(filePath, ImreadModes.Unchanged)) {
                // 然后用opencv来解析
                string result = DecodeByOpenCV(src);
                return result;
            }

        }
    }
}
