namespace TelegramSearchBot.LLM.Tests.Common;

/// <summary>
/// BDD风格测试基类
/// </summary>
public abstract class BddTestBase : IDisposable
{
    protected Exception? _caughtException;
    protected bool _disposed;

    /// <summary>
    /// Given - 设置测试前提条件
    /// </summary>
    protected abstract Task Given();

    /// <summary>
    /// When - 执行被测试的操作
    /// </summary>
    protected abstract Task When();

    /// <summary>
    /// Then - 验证结果
    /// </summary>
    protected abstract Task Then();

    /// <summary>
    /// 执行完整的BDD测试流程
    /// </summary>
    public async Task RunTest()
    {
        try
        {
            await Given();
            await When();
        }
        catch (Exception ex)
        {
            _caughtException = ex;
        }
        finally
        {
            await Then();
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // 清理托管资源
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
} 