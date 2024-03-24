using FFMpegCore.Extend;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;

namespace TelegramSearchBot.Manager {
    public class WhisperManager {


        const string modelName = "ggml-medium.bin";
        public string modelsDir { get; set; } 
        public string modelPath { get; set; } 

        const GgmlType ggmlType = GgmlType.Medium;
        private ILogger<WhisperManager> logger { get; set; }
        public WhisperManager(ILogger<WhisperManager> logger) {
            this.logger = logger;
            modelsDir = Path.Combine(Env.WorkDir, "Models");
            modelPath = Path.Combine(modelsDir, modelName);
        }
        
        async Task DownloadModelAsync(GgmlType modelType, string modelFileName, string targetModelsDir) {
            if (!Directory.Exists(targetModelsDir)) {
                Directory.CreateDirectory(targetModelsDir);
            }
            Console.WriteLine($"Model {modelName} not found. Downloading...");
            await using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(modelType);
            await using var fileWriter = File.OpenWrite(Path.Combine(targetModelsDir, modelName));
            await modelStream.CopyToAsync(fileWriter);
            Console.WriteLine($"Model {modelName} downloaded to {targetModelsDir}");
        }

        public async Task<string> ExecuteAsync(Stream wavStream) {
            TimeSpan timeTaken;
            var startTime = DateTime.UtcNow;
            if (!File.Exists(modelPath)) {
                await DownloadModelAsync(ggmlType, modelName, modelsDir);
                timeTaken = DateTime.UtcNow - startTime;
                logger.LogInformation($"Time Taken to Download: {timeTaken.TotalSeconds} Seconds");
            }
            using var whisperFactory = WhisperFactory.FromPath(modelPath);

            // This section creates the processor object which is used to process the audio file, it uses language `auto` to detect the language of the audio file.
            await using var processor = whisperFactory.CreateBuilder()
                                                      .WithSpeedUp2x()
                                                      .WithThreads(16)
                                                      //.WithPrompt(prompt)
                                                      .Build();

            timeTaken = DateTime.UtcNow - startTime;
            logger.LogInformation("Time Taken to init Whisper: {0}", timeTaken.ToLongString());


            // This section sets the wavStream to the beginning of the stream. (This is required because the wavStream was written to in the previous section)
            wavStream.Seek(0, SeekOrigin.Begin);

            logger.LogInformation("⟫ Starting Whisper processing...");

            startTime = DateTime.UtcNow;

            var ToReturn = new List<string>();

            // This section processes the audio file and prints the results (start time, end time and text) to the console.
            await foreach (var result in processor.ProcessAsync(wavStream)) {
                timeTaken = DateTime.UtcNow - startTime;
                logger.LogInformation($"{result.Start.ToLongString()}-->{result.End.ToLongString()}: {result.Text,-150} [{timeTaken.ToLongString()}]");
                ToReturn.Add(result.Text);
                startTime = DateTime.UtcNow;
            }

            logger.LogInformation("⟫ Completed Whisper processing...");
            return string.Join("\n", ToReturn);
        }
    }
}
