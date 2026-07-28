using Serilog.Events;
using TelegramSearchBot.Common;
using Xunit;

namespace TelegramSearchBot.Test.Common {
    public class EnvBotApiEndpointTests {
        [Fact]
        public void ResolveBotApiEndpoint_UsesEmbeddedLocalApiWhenEnabled() {
            var result = Env.ResolveBotApiEndpoint(new Config {
                BaseUrl = "https://api.telegram.org",
                EnableLocalBotAPI = true,
                LocalBotApiPort = 8081,
                ExternalLocalBotApiBaseUrl = "http://127.0.0.1:8082/"
            });

            Assert.Equal("http://127.0.0.1:8081", result.BaseUrl);
            Assert.True(result.IsLocalApi);
            Assert.Equal("http://127.0.0.1:8082", result.ExternalLocalBotApiBaseUrl);
        }

        [Fact]
        public void ResolveBotApiEndpoint_UsesExternalLocalApiWhenConfigured() {
            var result = Env.ResolveBotApiEndpoint(new Config {
                BaseUrl = "https://api.telegram.org",
                IsLocalAPI = false,
                EnableLocalBotAPI = false,
                ExternalLocalBotApiBaseUrl = "http://127.0.0.1:8082/"
            });

            Assert.Equal("http://127.0.0.1:8082", result.BaseUrl);
            Assert.True(result.IsLocalApi);
        }

        [Fact]
        public void ResolveBotApiEndpoint_FallsBackToConfiguredBaseUrlWhenNoLocalApiConfigured() {
            var result = Env.ResolveBotApiEndpoint(new Config {
                BaseUrl = "https://api.telegram.org/",
                IsLocalAPI = false,
                EnableLocalBotAPI = false
            });

            Assert.Equal("https://api.telegram.org", result.BaseUrl);
            Assert.False(result.IsLocalApi);
        }

        [Fact]
        public void ResolveUpdateBaseUrl_TrimsTrailingSlash() {
            var result = Env.ResolveUpdateBaseUrl(new Config {
                UpdateBaseUrl = "https://clickonce.miaostay.com/TelegramSearchBot/"
            });

            Assert.Equal("https://clickonce.miaostay.com/TelegramSearchBot", result);
        }

        [Fact]
        public void ResolveUpdateBaseUrl_FallsBackToDefaultWhenBlank() {
            var result = Env.ResolveUpdateBaseUrl(new Config {
                UpdateBaseUrl = "   "
            });

            Assert.Equal(Env.DefaultUpdateBaseUrl, result);
        }

        [Fact]
        public void ResolveUpdateBaseUrl_FallsBackToDefaultWhenUrlIsNotHttpsOrLoopback() {
            var result = Env.ResolveUpdateBaseUrl(new Config {
                UpdateBaseUrl = "http://example.com/update"
            });

            Assert.Equal(Env.DefaultUpdateBaseUrl, result);
        }

        [Theory]
        [InlineData("Verbose", LogEventLevel.Verbose)]
        [InlineData("Error", LogEventLevel.Error)]
        public void ResolveSerilogMinimumLevel_ReturnsDefinedLogLevel(string logLevel, LogEventLevel expected) {
            var result = Env.ResolveSerilogMinimumLevel(logLevel);

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("999")]
        [InlineData("NotARealLevel")]
        [InlineData("")]
        public void ResolveSerilogMinimumLevel_FallsBackToVerboseForInvalidValue(string? logLevel) {
            var result = Env.ResolveSerilogMinimumLevel(logLevel);

            Assert.Equal(LogEventLevel.Verbose, result);
        }
    }
}
