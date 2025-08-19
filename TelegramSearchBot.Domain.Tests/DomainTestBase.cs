using Xunit;
using System;

namespace TelegramSearchBot.Domain.Tests
{
    /// <summary>
    /// DDD领域测试基类，提供通用的测试设置和清理逻辑
    /// </summary>
    public abstract class DomainTestBase
    {
        protected DomainTestBase()
        {
            // 测试初始化逻辑
            SetupTestEnvironment();
        }

        /// <summary>
        /// 设置测试环境
        /// </summary>
        protected virtual void SetupTestEnvironment()
        {
            // 可以在这里设置测试特定的环境变量或配置
            // 例如：设置时间区域、文化信息等
        }

        /// <summary>
        /// 创建测试用的DateTime
        /// </summary>
        protected static DateTime CreateTestDateTime(int year = 2024, int month = 1, int day = 1, int hour = 12, int minute = 0, int second = 0)
        {
            return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
        }

        /// <summary>
        /// 创建测试用的DateTime（本地时间）
        /// </summary>
        protected static DateTime CreateTestLocalDateTime(int year = 2024, int month = 1, int day = 1, int hour = 12, int minute = 0, int second = 0)
        {
            return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local);
        }

        /// <summary>
        /// 验证异常消息
        /// </summary>
        protected static void AssertExceptionMessageContains<TException>(TException exception, string expectedMessage) where TException : Exception
        {
            Assert.NotNull(exception);
            Assert.Contains(expectedMessage, exception.Message);
        }

        /// <summary>
        /// 验证异常参数名
        /// </summary>
        protected static void AssertExceptionParamName<TException>(TException exception, string expectedParamName) where TException : ArgumentException
        {
            Assert.NotNull(exception);
            Assert.Equal(expectedParamName, exception.ParamName);
        }

        /// <summary>
        /// 验证对象不为null
        /// </summary>
        protected static void AssertNotNull<T>(T obj, string paramName = null) where T : class
        {
            Assert.NotNull(obj);
        }

        /// <summary>
        /// 验证对象为null
        /// </summary>
        protected static void AssertNull<T>(T obj, string paramName = null) where T : class
        {
            Assert.Null(obj);
        }

        /// <summary>
        /// 验证值在指定范围内
        /// </summary>
        protected static void AssertInRange<T>(T value, T min, T max) where T : IComparable<T>
        {
            Assert.True(value.CompareTo(min) >= 0, $"值 {value} 小于最小值 {min}");
            Assert.True(value.CompareTo(max) <= 0, $"值 {value} 大于最大值 {max}");
        }

        /// <summary>
        /// 验证值大于指定值
        /// </summary>
        protected static void AssertGreaterThan<T>(T value, T expected) where T : IComparable<T>
        {
            Assert.True(value.CompareTo(expected) > 0, $"值 {value} 不大于 {expected}");
        }

        /// <summary>
        /// 验证值小于指定值
        /// </summary>
        protected static void AssertLessThan<T>(T value, T expected) where T : IComparable<T>
        {
            Assert.True(value.CompareTo(expected) < 0, $"值 {value} 不小于 {expected}");
        }

        /// <summary>
        /// 验证两个对象相等
        /// </summary>
        protected static void AssertEqual<T>(T expected, T actual, string message = null)
        {
            if (message != null)
            {
                Assert.Equal(expected, actual);
            }
            else
            {
                Assert.Equal(expected, actual);
            }
        }

        /// <summary>
        /// 验证两个对象不相等
        /// </summary>
        protected static void AssertNotEqual<T>(T expected, T actual, string message = null)
        {
            if (message != null)
            {
                Assert.NotEqual(expected, actual);
            }
            else
            {
                Assert.NotEqual(expected, actual);
            }
        }

        /// <summary>
        /// 验证条件为true
        /// </summary>
        protected static void AssertTrue(bool condition, string message = null)
        {
            if (message != null)
            {
                Assert.True(condition, message);
            }
            else
            {
                Assert.True(condition);
            }
        }

        /// <summary>
        /// 验证条件为false
        /// </summary>
        protected static void AssertFalse(bool condition, string message = null)
        {
            if (message != null)
            {
                Assert.False(condition, message);
            }
            else
            {
                Assert.False(condition);
            }
        }
    }
}