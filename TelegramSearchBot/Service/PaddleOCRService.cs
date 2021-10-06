using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Threading.Tasks;
using Tesseract;
using System.Drawing.Imaging;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using System.Net.Http;
using Newtonsoft.Json;
using System.Threading;

namespace TelegramSearchBot.Service {
    public class PaddleOCRService : IStreamService, IService {
        public string ServiceName => "PaddleOCRService";
        private static SemaphoreSlim semaphore = new SemaphoreSlim(Env.PaddleOCRAPIParallel);


        /// <summary>
        /// 按理说是进来文件出去字符的
        /// </summary>
        /// <param name="messageOption"></param>
        /// <returns></returns>
        public async Task<string> ExecuteAsync(Stream file) {
            await semaphore.WaitAsync().ConfigureAwait(false);
            if (Env.PaddleOCRAPI.Equals(string.Empty)) {
                return "";
            }
            var stream = new MemoryStream();
            var tg_img = Image.FromStream(file);
            tg_img.Save(stream, System.Drawing.Imaging.ImageFormat.Jpeg);
            var tg_img_arr = stream.ToArray();
            var tg_img_base64 = Convert.ToBase64String(tg_img_arr);
            var postJson = new PaddleOCRPost() { Images = new List<string>() { tg_img_base64 } };
            var client = new HttpClient();
            var response = await client.PostAsync(Env.PaddleOCRAPI, new StringContent(JsonConvert.SerializeObject(postJson), Encoding.UTF8, "application/json"));
            var responseText = await response.Content.ReadAsStringAsync();
            semaphore.Release();
            var responseJson = JsonConvert.DeserializeObject<PaddleOCRResult>(responseText);
            int status;
            if (int.TryParse(responseJson.Status, out status) && status == 0) {
                var StringList = new List<string>();
                foreach (var e in responseJson.Results) {
                    foreach (var f in e) {
                        StringList.Add(f.Text);
                    }
                }
                return string.Join(" ", StringList);
            } else {
                return "";
            }
        }
    }
}
