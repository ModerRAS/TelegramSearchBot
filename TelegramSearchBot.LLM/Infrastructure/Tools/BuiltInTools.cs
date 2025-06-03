using TelegramSearchBot.LLM.Domain.Services;
using TelegramSearchBot.LLM.Domain.ValueObjects;

namespace TelegramSearchBot.LLM.Infrastructure.Tools;

/// <summary>
/// 内置工具注册器
/// </summary>
public static class BuiltInTools
{
    /// <summary>
    /// 注册所有内置工具
    /// </summary>
    public static void RegisterAll(IToolInvocationService toolService)
    {
        RegisterTimeTools(toolService);
        RegisterCalculatorTools(toolService);
        RegisterTextTools(toolService);
    }

    /// <summary>
    /// 注册时间相关工具
    /// </summary>
    public static void RegisterTimeTools(IToolInvocationService toolService)
    {
        // 获取当前时间
        var getCurrentTimeDefinition = new ToolDefinition(
            Name: "get_current_time",
            Description: "获取当前日期和时间",
            Parameters: new List<ToolParameter>
            {
                new("timezone", ToolParameterType.String, "时区，默认为系统时区", false, "Local")
            },
            Category: "时间工具");

        toolService.RegisterTool(getCurrentTimeDefinition, async parameters =>
        {
            var timezone = parameters.TryGetValue("timezone", out var tz) ? tz.ToString() : "Local";
            
            var currentTime = timezone?.ToLower() switch
            {
                "utc" => DateTime.UtcNow,
                "local" or _ => DateTime.Now
            };

            return new
            {
                current_time = currentTime.ToString("yyyy-MM-dd HH:mm:ss"),
                timezone = timezone,
                day_of_week = currentTime.DayOfWeek.ToString(),
                timestamp = currentTime.Ticks
            };
        });

        // 时间格式转换
        var formatTimeDefinition = new ToolDefinition(
            Name: "format_time",
            Description: "格式化时间字符串",
            Parameters: new List<ToolParameter>
            {
                new("time_string", ToolParameterType.String, "要格式化的时间字符串", true),
                new("format", ToolParameterType.String, "目标格式", false, "yyyy-MM-dd HH:mm:ss")
            },
            Category: "时间工具");

        toolService.RegisterTool(formatTimeDefinition, async parameters =>
        {
            var timeString = parameters["time_string"].ToString()!;
            var format = parameters.TryGetValue("format", out var fmt) ? fmt.ToString() : "yyyy-MM-dd HH:mm:ss";

            if (DateTime.TryParse(timeString, out var dateTime))
            {
                return new
                {
                    original = timeString,
                    formatted = dateTime.ToString(format),
                    success = true
                };
            }

            return new
            {
                original = timeString,
                error = "无法解析时间字符串",
                success = false
            };
        });
    }

    /// <summary>
    /// 注册计算器工具
    /// </summary>
    public static void RegisterCalculatorTools(IToolInvocationService toolService)
    {
        // 基础计算器
        var calculatorDefinition = new ToolDefinition(
            Name: "calculator",
            Description: "执行基础数学计算（支持加减乘除、括号等）",
            Parameters: new List<ToolParameter>
            {
                new("expression", ToolParameterType.String, "数学表达式，如 '2 + 3 * 4'", true)
            },
            Category: "计算工具");

        toolService.RegisterTool(calculatorDefinition, async parameters =>
        {
            var expression = parameters["expression"].ToString()!;
            
            try
            {
                // 简单的表达式计算器（实际项目中可使用更强大的库）
                var result = EvaluateExpression(expression);
                
                return new
                {
                    expression = expression,
                    result = result,
                    success = true
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    expression = expression,
                    error = ex.Message,
                    success = false
                };
            }
        });

        // 数学函数计算器
        var mathFunctionDefinition = new ToolDefinition(
            Name: "math_function",
            Description: "执行数学函数计算（如sin, cos, log等）",
            Parameters: new List<ToolParameter>
            {
                new("function", ToolParameterType.String, "数学函数名称", true),
                new("value", ToolParameterType.Number, "输入值", true),
                new("unit", ToolParameterType.String, "角度单位（degree/radian），适用于三角函数", false, "radian")
            },
            Category: "计算工具");

        toolService.RegisterTool(mathFunctionDefinition, async parameters =>
        {
            var function = parameters["function"].ToString()!.ToLower();
            var value = Convert.ToDouble(parameters["value"]);
            var unit = parameters.TryGetValue("unit", out var u) ? u.ToString()!.ToLower() : "radian";

            try
            {
                double result = function switch
                {
                    "sin" => Math.Sin(unit == "degree" ? value * Math.PI / 180 : value),
                    "cos" => Math.Cos(unit == "degree" ? value * Math.PI / 180 : value),
                    "tan" => Math.Tan(unit == "degree" ? value * Math.PI / 180 : value),
                    "log" => Math.Log10(value),
                    "ln" => Math.Log(value),
                    "sqrt" => Math.Sqrt(value),
                    "abs" => Math.Abs(value),
                    "round" => Math.Round(value),
                    "floor" => Math.Floor(value),
                    "ceil" => Math.Ceiling(value),
                    _ => throw new NotSupportedException($"不支持的函数: {function}")
                };

                return new
                {
                    function = function,
                    input_value = value,
                    result = result,
                    unit = unit,
                    success = true
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    function = function,
                    input_value = value,
                    error = ex.Message,
                    success = false
                };
            }
        });
    }

    /// <summary>
    /// 注册文本处理工具
    /// </summary>
    public static void RegisterTextTools(IToolInvocationService toolService)
    {
        // 文本统计
        var textStatsDefinition = new ToolDefinition(
            Name: "text_stats",
            Description: "分析文本统计信息（字符数、单词数、行数等）",
            Parameters: new List<ToolParameter>
            {
                new("text", ToolParameterType.String, "要分析的文本", true)
            },
            Category: "文本工具");

        toolService.RegisterTool(textStatsDefinition, async parameters =>
        {
            var text = parameters["text"].ToString()!;
            
            var charCount = text.Length;
            var charCountNoSpaces = text.Count(c => !char.IsWhiteSpace(c));
            var wordCount = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
            var lineCount = text.Split('\n').Length;
            var sentenceCount = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries).Length;

            return new
            {
                text_preview = text.Length > 100 ? text[..100] + "..." : text,
                character_count = charCount,
                character_count_no_spaces = charCountNoSpaces,
                word_count = wordCount,
                line_count = lineCount,
                sentence_count = sentenceCount,
                average_word_length = wordCount > 0 ? (double)charCountNoSpaces / wordCount : 0
            };
        });

        // Base64编码/解码
        var base64Definition = new ToolDefinition(
            Name: "base64_encode_decode",
            Description: "Base64编码或解码文本",
            Parameters: new List<ToolParameter>
            {
                new("text", ToolParameterType.String, "要处理的文本", true),
                new("operation", ToolParameterType.String, "操作类型：encode 或 decode", true)
            },
            Category: "文本工具");

        toolService.RegisterTool(base64Definition, async parameters =>
        {
            var text = parameters["text"].ToString()!;
            var operation = parameters["operation"].ToString()!.ToLower();

            try
            {
                string result = operation switch
                {
                    "encode" => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text)),
                    "decode" => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(text)),
                    _ => throw new ArgumentException("操作类型必须是 'encode' 或 'decode'")
                };

                return new
                {
                    original_text = text,
                    operation = operation,
                    result = result,
                    success = true
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    original_text = text,
                    operation = operation,
                    error = ex.Message,
                    success = false
                };
            }
        });
    }

    /// <summary>
    /// 简单的表达式计算器实现
    /// </summary>
    private static double EvaluateExpression(string expression)
    {
        // 注意：这是一个简化的实现，生产环境中应使用更安全和完整的表达式求值器
        // 例如：System.Data.DataTable.Compute 或专门的数学表达式库
        
        var table = new System.Data.DataTable();
        var result = table.Compute(expression, "");
        
        return Convert.ToDouble(result);
    }
} 