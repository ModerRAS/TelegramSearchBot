using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Threading.Tasks;
using Tesseract;

namespace TelegramSearchBot.Service {
    class AutoOCRService {
        /// <summary>
        /// 按理说是进来文件出去字符的
        /// </summary>
        /// <param name="messageOption"></param>
        /// <returns></returns>
        public async Task<string> ExecuteAsync(MemoryStream file) {
            var stream = new MemoryStream();
            var tg_img = Image.FromStream(file);
            tg_img.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            stream.Position = 0;
            var texts = new List<string>();
            using (var engine = new TesseractEngine(@"/app/tessdata", "chi_sim", EngineMode.Default)) {
                using (var img = Pix.LoadFromMemory(stream.ToArray())) {
                    using (var page = engine.Process(img)) {
                        var text = page.GetText();
                        //Console.WriteLine("Mean confidence: {0}", page.GetMeanConfidence());

                        //Console.WriteLine("Text (GetText): \r\n{0}", text);
                        //Console.WriteLine("Text (iterator):");
                        using (var iter = page.GetIterator()) {
                            iter.Begin();

                            do {
                                do {
                                    do {
                                        do {
                                            //if (iter.IsAtBeginningOf(PageIteratorLevel.Block)) {
                                            //    Console.WriteLine("<BLOCK>");
                                            //}
                                            var inner_text = iter.GetText(PageIteratorLevel.Word);

                                            texts.Add(inner_text);

                                            //Console.Write(inner_text);
                                            //Console.Write(" ");

                                            //if (iter.IsAtFinalOf(PageIteratorLevel.TextLine, PageIteratorLevel.Word)) {
                                            //    Console.WriteLine();
                                            //}
                                        } while (iter.Next(PageIteratorLevel.TextLine, PageIteratorLevel.Word));

                                        //if (iter.IsAtFinalOf(PageIteratorLevel.Para, PageIteratorLevel.TextLine)) {
                                        //    Console.WriteLine();
                                        //}
                                    } while (iter.Next(PageIteratorLevel.Para, PageIteratorLevel.TextLine));
                                } while (iter.Next(PageIteratorLevel.Block, PageIteratorLevel.Para));
                            } while (iter.Next(PageIteratorLevel.Block));

                        }
                    }
                }
            }
            return string.Join("", texts);
        }
    }
}
