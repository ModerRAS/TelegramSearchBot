using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using TelegramSearchBot.Common.Interface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Test.Helpers
{
    /// <summary>
    /// 测试配置辅助类，提供统一的测试配置管理
    /// </summary>
    public static class TestConfigurationHelper
    {
        private static IConfiguration? _configuration;
        private static string? _tempConfigPath;

        /// <summary>
        /// 获取测试配置
        /// </summary>
        /// <returns>配置对象</returns>
        public static IConfiguration GetConfiguration()
        {
            if (_configuration == null)
            {
                var builder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .AddJsonFile("appsettings.Test.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables()
                    .AddInMemoryCollection(GetDefaultTestSettings());

                _configuration = builder.Build();
            }

            return _configuration;
        }

        /// <summary>
        /// 创建临时配置文件
        /// </summary>
        /// <param name="configData">配置数据</param>
        /// <returns>配置文件路径</returns>
        public static string CreateTempConfigFile(Dictionary<string, string>? configData = null)
        {
            if (_tempConfigPath != null && File.Exists(_tempConfigPath))
            {
                File.Delete(_tempConfigPath);
            }

            _tempConfigPath = Path.GetTempFileName();
            var settings = configData ?? GetDefaultTestSettings();

            var configContent = @"{
  ""Telegram"": {
    ""BotToken"": ""test_bot_token_123456789"",
    ""AdminId"": 123456789
  },
  ""AI"": {
    ""OllamaModelName"": ""llama3.2"",
    ""OpenAIModelName"": ""gpt-3.5-turbo"",
    ""GeminiModelName"": ""gemini-pro"",
    ""EnableAutoOCR"": true,
    ""EnableAutoASR"": true,
    ""EnableVideoASR"": false
  },
  ""Search"": {
    ""MaxResults"": 50,
    ""DefaultPageSize"": 10,
    ""EnableVectorSearch"": true,
    ""EnableFullTextSearch"": true
  },
  ""Database"": {
    ""ConnectionString"": ""Data Source=test.db"",
    ""EnableWAL"": true,
    ""MaxPoolSize"": 100
  },
  ""Logging"": {
    ""LogLevel"": {
      ""Default"": ""Information"",
      ""Microsoft"": ""Warning"",
      ""System"": ""Warning""
    }
  }
}";

            // 合并自定义配置
            if (configData != null && configData.Any())
            {
                var configDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(configContent);
                if (configDict != null)
                {
                    MergeConfigurations(configDict, configData);
                    configContent = System.Text.Json.JsonSerializer.Serialize(configDict, new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                }
            }

            File.WriteAllText(_tempConfigPath, configContent);
            return _tempConfigPath;
        }

        /// <summary>
        /// 清理临时配置文件
        /// </summary>
        public static void CleanupTempConfigFile()
        {
            if (_tempConfigPath != null && File.Exists(_tempConfigPath))
            {
                File.Delete(_tempConfigPath);
                _tempConfigPath = null;
            }
        }

        /// <summary>
        /// 获取默认测试设置
        /// </summary>
        /// <returns>默认设置字典</returns>
        public static Dictionary<string, string> GetDefaultTestSettings()
        {
            return new Dictionary<string, string>
            {
                ["Telegram:BotToken"] = "test_bot_token_123456789",
                ["Telegram:AdminId"] = "123456789",
                ["AI:OllamaModelName"] = "llama3.2",
                ["AI:OpenAIModelName"] = "gpt-3.5-turbo",
                ["AI:GeminiModelName"] = "gemini-pro",
                ["AI:EnableAutoOCR"] = "true",
                ["AI:EnableAutoASR"] = "true",
                ["AI:EnableVideoASR"] = "false",
                ["Search:MaxResults"] = "50",
                ["Search:DefaultPageSize"] = "10",
                ["Search:EnableVectorSearch"] = "true",
                ["Search:EnableFullTextSearch"] = "true",
                ["Database:ConnectionString"] = "Data Source=test.db",
                ["Database:EnableWAL"] = "true",
                ["Database:MaxPoolSize"] = "100",
                ["Logging:LogLevel:Default"] = "Information",
                ["Logging:LogLevel:Microsoft"] = "Warning",
                ["Logging:LogLevel:System"] = "Warning"
            };
        }

        /// <summary>
        /// 获取测试用的Bot配置
        /// </summary>
        /// <returns>Bot配置</returns>
        public static BotConfig GetTestBotConfig()
        {
            return new BotConfig
            {
                BotToken = "test_bot_token_123456789",
                AdminId = 123456789,
                EnableAutoOCR = true,
                EnableAutoASR = true,
                EnableVideoASR = false,
                OllamaModelName = "llama3.2",
                OpenAIModelName = "gpt-3.5-turbo",
                GeminiModelName = "gemini-pro",
                MaxResults = 50,
                DefaultPageSize = 10,
                EnableVectorSearch = true,
                EnableFullTextSearch = true
            };
        }

        /// <summary>
        /// 获取测试用的LLM通道配置
        /// </summary>
        /// <returns>LLM通道配置列表</returns>
        public static List<LLMChannel> GetTestLLMChannels()
        {
            return new List<LLMChannel>
            {
                new LLMChannel
                {
                    Name = "OpenAI Test Channel",
                    Gateway = "https://api.openai.com/v1",
                    ApiKey = "test-openai-key",
                    Provider = LLMProvider.OpenAI,
                    Parallel = 1,
                    Priority = 1,
                    IsEnabled = true,
                    ModelName = "gpt-3.5-turbo",
                    MaxTokens = 4096,
                    Temperature = 0.7
                },
                new LLMChannel
                {
                    Name = "Ollama Test Channel",
                    Gateway = "http://localhost:11434",
                    ApiKey = "",
                    Provider = LLMProvider.Ollama,
                    Parallel = 2,
                    Priority = 2,
                    IsEnabled = true,
                    ModelName = "llama3.2",
                    MaxTokens = 4096,
                    Temperature = 0.7
                },
                new LLMChannel
                {
                    Name = "Gemini Test Channel",
                    Gateway = "https://generativelanguage.googleapis.com/v1beta",
                    ApiKey = "test-gemini-key",
                    Provider = LLMProvider.Gemini,
                    Parallel = 1,
                    Priority = 3,
                    IsEnabled = false,
                    ModelName = "gemini-pro",
                    MaxTokens = 8192,
                    Temperature = 0.5
                }
            };
        }

        /// <summary>
        /// 获取测试用的搜索配置
        /// </summary>
        /// <returns>搜索配置</returns>
        public static SearchConfig GetTestSearchConfig()
        {
            return new SearchConfig
            {
                MaxResults = 50,
                DefaultPageSize = 10,
                EnableVectorSearch = true,
                EnableFullTextSearch = true,
                VectorSearchWeight = 0.7f,
                FullTextSearchWeight = 0.3f,
                MinScoreThreshold = 0.5f,
                EnableHighlighting = true,
                EnableSnippetGeneration = true,
                SnippetLength = 200
            };
        }

        /// <summary>
        /// 获取测试用的数据库配置
        /// </summary>
        /// <returns>数据库配置</returns>
        public static DatabaseConfig GetTestDatabaseConfig()
        {
            return new DatabaseConfig
            {
                ConnectionString = "Data Source=test.db",
                EnableWAL = true,
                MaxPoolSize = 100,
                CommandTimeout = 30,
                EnableSensitiveDataLogging = false,
                EnableDetailedErrors = false
            };
        }

        /// <summary>
        /// 获取测试用的环境变量
        /// </summary>
        /// <returns>环境变量字典</returns>
        public static Dictionary<string, string> GetTestEnvironmentVariables()
        {
            return new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Test",
                ["TELEGRAM_BOT_TOKEN"] = "test_bot_token_123456789",
                ["TELEGRAM_ADMIN_ID"] = "123456789",
                ["OPENAI_API_KEY"] = "test-openai-key",
                ["OLLAMA_BASE_URL"] = "http://localhost:11434",
                ["GEMINI_API_KEY"] = "test-gemini-key",
                ["DATABASE_CONNECTION_STRING"] = "Data Source=test.db",
                ["LOG_LEVEL"] = "Information"
            };
        }

        /// <summary>
        /// 创建配置服务
        /// </summary>
        /// <param name="customSettings">自定义设置</param>
        /// <returns>配置服务</returns>
        public static IEnvService CreateEnvService(Dictionary<string, string>? customSettings = null)
        {
            var settings = customSettings ?? GetDefaultTestSettings();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            return new TestEnvService(configuration);
        }

        /// <summary>
        /// 获取测试用的应用配置
        /// </summary>
        /// <returns>应用配置字典</returns>
        public static Dictionary<string, object> GetTestAppConfiguration()
        {
            return new Dictionary<string, object>
            {
                ["AppVersion"] = "1.0.0-test",
                ["Environment"] = "Test",
                ["DebugMode"] = true,
                ["EnableTestFeatures"] = true,
                ["TestTimeout"] = 30000,
                ["TestRetryCount"] = 3,
                ["TestDatabaseCleanup"] = true,
                ["TestLogCapture"] = true
            };
        }

        /// <summary>
        /// 合并配置字典
        /// </summary>
        /// <param name="target">目标字典</param>
        /// <param name="source">源字典</param>
        private static void MergeConfigurations(Dictionary<string, object> target, Dictionary<string, string> source)
        {
            foreach (var kvp in source)
            {
                var keys = kvp.Key.Split(':');
                var current = target;

                for (int i = 0; i < keys.Length - 1; i++)
                {
                    if (!current.ContainsKey(keys[i]))
                    {
                        current[keys[i]] = new Dictionary<string, object>();
                    }

                    if (current[keys[i]] is Dictionary<string, object> nested)
                    {
                        current = nested;
                    }
                    else
                    {
                        break;
                    }
                }

                var lastKey = keys.Last();
                if (current.ContainsKey(lastKey) && current[lastKey] is Dictionary<string, object>)
                {
                    // 如果存在嵌套字典，保持原有结构
                    continue;
                }
                else
                {
                    current[lastKey] = kvp.Value;
                }
            }
        }

        /// <summary>
        /// 验证配置有效性
        /// </summary>
        /// <param name="configuration">配置对象</param>
        /// <returns>是否有效</returns>
        public static bool ValidateConfiguration(IConfiguration configuration)
        {
            var botToken = configuration["Telegram:BotToken"];
            var adminId = configuration["Telegram:AdminId"];

            if (string.IsNullOrEmpty(botToken) || botToken == "test_bot_token_123456789")
            {
                return false;
            }

            if (!long.TryParse(adminId, out var adminIdValue) || adminIdValue <= 0)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 获取配置验证错误信息
        /// </summary>
        /// <param name="configuration">配置对象</param>
        /// <returns>错误信息列表</returns>
        public static List<string> GetConfigurationValidationErrors(IConfiguration configuration)
        {
            var errors = new List<string>();

            var botToken = configuration["Telegram:BotToken"];
            if (string.IsNullOrEmpty(botToken))
            {
                errors.Add("BotToken is required");
            }
            else if (botToken == "test_bot_token_123456789")
            {
                errors.Add("BotToken is using test value");
            }

            var adminId = configuration["Telegram:AdminId"];
            if (string.IsNullOrEmpty(adminId))
            {
                errors.Add("AdminId is required");
            }
            else if (!long.TryParse(adminId, out var adminIdValue) || adminIdValue <= 0)
            {
                errors.Add("AdminId must be a positive integer");
            }

            return errors;
        }
    }

    /// <summary>
    /// 测试用的环境服务实现
    /// </summary>
    internal class TestEnvService : IEnvService
    {
        private readonly IConfiguration _configuration;

        public TestEnvService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string Get(string key)
        {
            return _configuration[key] ?? string.Empty;
        }

        public T Get<T>(string key)
        {
            var value = _configuration[key];
            if (string.IsNullOrEmpty(value))
            {
                return default(T);
            }

            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return default(T);
            }
        }

        public bool Contains(string key)
        {
            return !string.IsNullOrEmpty(_configuration[key]);
        }

        public void Set(string key, string value)
        {
            // 测试环境不支持设置值
        }

        public void Remove(string key)
        {
            // 测试环境不支持删除值
        }

        public IEnumerable<string> GetKeys()
        {
            return _configuration.AsEnumerable().Select(x => x.Key);
        }

        public void Reload()
        {
            // 测试环境不支持重新加载
        }
    }

    /// <summary>
    /// 配置类定义
    /// </summary>
    public class BotConfig
    {
        public string BotToken { get; set; } = string.Empty;
        public long AdminId { get; set; }
        public bool EnableAutoOCR { get; set; }
        public bool EnableAutoASR { get; set; }
        public bool EnableVideoASR { get; set; }
        public string OllamaModelName { get; set; } = "llama3.2";
        public string OpenAIModelName { get; set; } = "gpt-3.5-turbo";
        public string GeminiModelName { get; set; } = "gemini-pro";
        public int MaxResults { get; set; } = 50;
        public int DefaultPageSize { get; set; } = 10;
        public bool EnableVectorSearch { get; set; } = true;
        public bool EnableFullTextSearch { get; set; } = true;
    }

    public class SearchConfig
    {
        public int MaxResults { get; set; } = 50;
        public int DefaultPageSize { get; set; } = 10;
        public bool EnableVectorSearch { get; set; } = true;
        public bool EnableFullTextSearch { get; set; } = true;
        public float VectorSearchWeight { get; set; } = 0.7f;
        public float FullTextSearchWeight { get; set; } = 0.3f;
        public float MinScoreThreshold { get; set; } = 0.5f;
        public bool EnableHighlighting { get; set; } = true;
        public bool EnableSnippetGeneration { get; set; } = true;
        public int SnippetLength { get; set; } = 200;
    }

    public class DatabaseConfig
    {
        public string ConnectionString { get; set; } = string.Empty;
        public bool EnableWAL { get; set; } = true;
        public int MaxPoolSize { get; set; } = 100;
        public int CommandTimeout { get; set; } = 30;
        public bool EnableSensitiveDataLogging { get; set; } = false;
        public bool EnableDetailedErrors { get; set; } = false;
    }
}