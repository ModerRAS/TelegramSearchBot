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
            string modelDir = Path.Combine(Env.WorkDir, "wechat_qrcode");
            try {
                if (!Directory.Exists(modelDir)) {
                    Directory.CreateDirectory(modelDir);
                    logger.LogInformation($"已创建二维码模型目录: {modelDir}");
                }
                var client = new WebClient();
                if (!File.Exists(_wechat_QCODE_detector_prototxt_path)) {
                    client.DownloadFile(detect_prototxt_url, _wechat_QCODE_detector_prototxt_path);
                    logger.LogInformation("已下载detect.prototxt");
                }
                if (!File.Exists(_wechat_QCODE_detector_caffe_model_path)) {
                    client.DownloadFile(detect_caffe_model_url, _wechat_QCODE_detector_caffe_model_path);
                    logger.LogInformation("已下载detect.caffemodel");
                }
                if (!File.Exists(_wechat_QCODE_super_resolution_prototxt_path)) {
                    client.DownloadFile(sr_prototxt_url, _wechat_QCODE_super_resolution_prototxt_path);
                    logger.LogInformation("已下载sr.prototxt");
                }
                if (!File.Exists(_wechat_QCODE_super_resolution_caffe_model_path)) {
                    client.DownloadFile(sr_caffe_model_url, _wechat_QCODE_super_resolution_caffe_model_path);
                    logger.LogInformation("已下载sr.caffemodel");
                }
                // 检查所有模型文件是否存在
                if (!File.Exists(_wechat_QCODE_detector_prototxt_path) ||
                    !File.Exists(_wechat_QCODE_detector_caffe_model_path) ||
                    !File.Exists(_wechat_QCODE_super_resolution_prototxt_path) ||
                    !File.Exists(_wechat_QCODE_super_resolution_caffe_model_path)) {
                    throw new FileNotFoundException("二维码识别模型文件缺失，请检查网络或手动下载到 wechat_qrcode 目录");
                }
                opencvDecoder = WeChatQRCode.Create(
                    _wechat_QCODE_detector_prototxt_path,
                    _wechat_QCODE_detector_caffe_model_path,
                    _wechat_QCODE_super_resolution_prototxt_path,
                    _wechat_QCODE_super_resolution_caffe_model_path
                );
            } catch (Exception ex) {
                logger.LogError(ex, "二维码识别模型初始化失败");
                throw;
            }
            this.logger = logger;
        }
        public string DecodeByOpenCV(Mat img) {
            if (img == null) {
                logger.LogWarning("传入的图片为空，无法进行二维码识别");
                return string.Empty;
            }
            Mat[] rects;
            string[] texts;
            try {
                opencvDecoder.DetectAndDecode(img, out rects, out texts);
                img.Dispose();
            } catch (Exception ex) {
                logger.LogWarning(ex, "OpenCV二维码解码失败");
                return string.Empty;
            }
            if (rects.Length != 0) {
                return string.Join('\n', texts);
            } else {
                logger.LogInformation("未识别到二维码内容");
                return string.Empty;
            }
        }
        public async Task<string> ExecuteAsync(string filePath) {
            try {
                using (var src = Cv2.ImRead(filePath, ImreadModes.Unchanged)) {
                    if (src == null || src.Empty()) {
                        logger.LogWarning($"无法读取图片文件: {filePath}");
                        return string.Empty;
                    }
                    string result = DecodeByOpenCV(src);
                    return result;
                }
            } catch (Exception ex) {
                logger.LogError(ex, $"二维码识别时发生异常，文件: {filePath}");
                return string.Empty;
            }
        }
    }
}
