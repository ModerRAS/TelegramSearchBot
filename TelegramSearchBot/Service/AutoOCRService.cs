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

namespace TelegramSearchBot.Service {
    public class AutoOCRService : IStreamService {
        private async Task<Bitmap> ConvertToGray(Bitmap rgb_img) {
            Bitmap newBitmap = new Bitmap(rgb_img.Width, rgb_img.Height);

            //get a graphics object from the new image
            using (Graphics g = Graphics.FromImage(newBitmap)) {

                //create the grayscale ColorMatrix
                ColorMatrix colorMatrix = new ColorMatrix(
                   new float[][]
                   {
             new float[] {.3f, .3f, .3f, 0, 0},
             new float[] {.59f, .59f, .59f, 0, 0},
             new float[] {.11f, .11f, .11f, 0, 0},
             new float[] {0, 0, 0, 1, 0},
             new float[] {0, 0, 0, 0, 1}
                   });

                //create some image attributes
                using (ImageAttributes attributes = new ImageAttributes()) {

                    //set the color matrix attribute
                    attributes.SetColorMatrix(colorMatrix);

                    //draw the original image on the new image
                    //using the grayscale color matrix
                    g.DrawImage(rgb_img, new Rectangle(0, 0, rgb_img.Width, rgb_img.Height),
                                0, 0, rgb_img.Width, rgb_img.Height, GraphicsUnit.Pixel, attributes);
                }
            }
            return newBitmap;
        }
        /// <summary>
        /// 按理说是进来文件出去字符的
        /// </summary>
        /// <param name="messageOption"></param>
        /// <returns></returns>
        public async Task<string> ExecuteAsync(Stream file) {
            var stream = new MemoryStream();
            var tg_img = Image.FromStream(file);
            var bitmap = await ConvertToGray(new Bitmap(tg_img));
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
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
