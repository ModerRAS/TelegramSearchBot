using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;
using TelegramSearchBot.Service.Tools;
using Xunit;

namespace TelegramSearchBot.Test.Service.Tools {
    public class MusicGenerationToolServiceTests {
        [Theory]
        [InlineData("https://api.minimaxi.com", "https://api.minimaxi.com/v1/music_generation")]
        [InlineData("https://api.minimaxi.com/v1", "https://api.minimaxi.com/v1/music_generation")]
        [InlineData("https://example.test/custom/", "https://example.test/custom/v1/music_generation")]
        [InlineData("https://example.test/v1/music_generation", "https://example.test/v1/music_generation")]
        [InlineData("https://example.test/custom/music_generation", "https://example.test/custom/music_generation")]
        public void BuildMiniMaxMusicGenerationEndpoint_NormalizesGateway(string gateway, string expected) {
            Assert.Equal(expected, MusicGenerationToolService.BuildMiniMaxMusicGenerationEndpoint(gateway));
        }

        [Fact]
        public void BuildMiniMaxMusicGenerationEndpoint_WhenGatewayEmpty_Throws() {
            Assert.Throws<ArgumentException>(() => MusicGenerationToolService.BuildMiniMaxMusicGenerationEndpoint(" "));
        }

        [Fact]
        public void BuildMiniMaxMusicGenerationRequestBody_TextToMusicWithLyrics() {
            var body = MusicGenerationToolService.BuildMiniMaxMusicGenerationRequestBody(
                "music-2.6",
                "流行摇滚, 明亮, 适合夏夜",
                "[Verse]\n晚风吹过街角\n[Chorus]\n我们一起奔跑",
                false,
                false,
                "hex",
                44100,
                256000,
                "mp3",
                false,
                null,
                null,
                null);

            Assert.Equal("music-2.6", Value<string>(body, "model"));
            Assert.Equal("流行摇滚, 明亮, 适合夏夜", Value<string>(body, "prompt"));
            Assert.Equal("[Verse]\n晚风吹过街角\n[Chorus]\n我们一起奔跑", Value<string>(body, "lyrics"));
            Assert.Equal("hex", Value<string>(body, "output_format"));
            Assert.False(Value<bool>(body, "lyrics_optimizer"));
            Assert.False(Value<bool>(body, "is_instrumental"));
            Assert.Equal(44100, NestedValue<int>(body, "audio_setting", "sample_rate"));
            Assert.Equal(256000, NestedValue<int>(body, "audio_setting", "bitrate"));
            Assert.Equal("mp3", NestedValue<string>(body, "audio_setting", "format"));
        }

        [Fact]
        public void BuildMiniMaxMusicGenerationRequestBody_LyricsOptimizerAllowsEmptyLyrics() {
            var body = MusicGenerationToolService.BuildMiniMaxMusicGenerationRequestBody(
                "music-2.6-free",
                "独立民谣, 温柔女声, 咖啡馆雨夜",
                null,
                false,
                true,
                null,
                32000,
                128000,
                "wav",
                true,
                null,
                null,
                null);

            Assert.Equal("music-2.6-free", Value<string>(body, "model"));
            Assert.Null(body["lyrics"]);
            Assert.True(Value<bool>(body, "lyrics_optimizer"));
            Assert.True(Value<bool>(body, "aigc_watermark"));
            Assert.Equal("hex", Value<string>(body, "output_format"));
            Assert.Equal("wav", NestedValue<string>(body, "audio_setting", "format"));
        }

        [Fact]
        public void BuildMiniMaxMusicGenerationRequestBody_WhenLyricsMissingAndNoOptimizer_Throws() {
            Assert.Throws<ArgumentException>(() => MusicGenerationToolService.BuildMiniMaxMusicGenerationRequestBody(
                "music-2.6",
                "流行",
                null,
                false,
                false,
                "hex",
                44100,
                256000,
                "mp3",
                false,
                null,
                null,
                null));
        }

        [Fact]
        public void BuildMiniMaxMusicGenerationRequestBody_CoverWithFeatureIdRequiresLyrics() {
            var body = MusicGenerationToolService.BuildMiniMaxMusicGenerationRequestBody(
                "music-cover",
                "warm acoustic cover",
                "[Verse]\nnew words",
                false,
                false,
                "url",
                44100,
                256000,
                "mp3",
                false,
                null,
                null,
                "feature_123");

            Assert.Equal("music-cover", Value<string>(body, "model"));
            Assert.Equal("feature_123", Value<string>(body, "cover_feature_id"));
            Assert.Equal("[Verse]\nnew words", Value<string>(body, "lyrics"));
            Assert.Equal("url", Value<string>(body, "output_format"));
        }

        [Fact]
        public void BuildMiniMaxMusicGenerationRequestBody_CoverRequiresExactlyOneSource() {
            Assert.Throws<ArgumentException>(() => MusicGenerationToolService.BuildMiniMaxMusicGenerationRequestBody(
                "music-cover",
                "warm acoustic cover",
                null,
                false,
                false,
                "url",
                44100,
                256000,
                "mp3",
                false,
                null,
                null,
                null));
        }

        [Fact]
        public void EnsureMiniMaxResponseSucceeded_WhenStatusNonZero_Throws() {
            var ex = Assert.Throws<InvalidOperationException>(() => MusicGenerationToolService.EnsureMiniMaxResponseSucceeded(
                @"{""base_resp"":{""status_code"":2013,""status_msg"":""bad args""}}"));

            Assert.Contains("2013", ex.Message);
            Assert.Contains("bad args", ex.Message);
        }

        [Fact]
        public void ExtractMusic_ReadsHexAndExtraInfo() {
            var music = MusicGenerationToolService.ExtractMusic(
                @"{""data"":{""audio"":""000102ff"",""status"":2},""extra_info"":{""music_duration"":25364,""music_sample_rate"":44100,""music_channel"":2,""bitrate"":256000},""base_resp"":{""status_code"":0,""status_msg"":""success""}}",
                "hex",
                "mp3");

            Assert.Equal(new byte[] { 0, 1, 2, 255 }, music.Bytes);
            Assert.Null(music.Url);
            Assert.Equal(25364, music.DurationMilliseconds);
            Assert.Equal(44100, music.SampleRate);
            Assert.Equal(2, music.Channels);
            Assert.Equal(256000, music.Bitrate);
        }

        [Fact]
        public async Task MusicGenerationSettings_UsesGroupModelBeforeDefault() {
            await using var dbContext = CreateDbContext();
            dbContext.AppConfigurationItems.Add(new AppConfigurationItem {
                Key = MusicGenerationToolSettingsService.ModelNameKey,
                Value = "music-2.6"
            });
            dbContext.GroupSettings.Add(new GroupSettings {
                GroupId = -100,
                MusicGenerationModelName = "music-2.6-free"
            });
            await dbContext.SaveChangesAsync();

            var settings = CreateSettingsService(dbContext);

            Assert.Equal("music-2.6-free", await settings.GetModelNameAsync(-100));
            Assert.Equal("music-2.6", await settings.GetModelNameAsync(-200));
        }

        private static T Value<T>(JObject obj, string key) {
            var token = obj[key];
            Assert.NotNull(token);
            return token!.ToObject<T>()!;
        }

        private static T NestedValue<T>(JObject obj, string parentKey, string key) {
            var parent = obj[parentKey] as JObject;
            Assert.NotNull(parent);
            return Value<T>(parent!, key);
        }

        private static DataDbContext CreateDbContext() {
            var options = new DbContextOptionsBuilder<DataDbContext>()
                .UseInMemoryDatabase($"MusicGenerationToolServiceTests_{Guid.NewGuid():N}")
                .Options;
            return new DataDbContext(options);
        }

        private static MusicGenerationToolSettingsService CreateSettingsService(DataDbContext dbContext) {
            return new MusicGenerationToolSettingsService(
                dbContext,
                new Mock<IConnectionMultiplexer>().Object,
                NullLogger<MusicGenerationToolSettingsService>.Instance);
        }
    }
}
