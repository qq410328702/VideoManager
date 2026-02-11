using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace VideoManager.Services;

/// <summary>
/// SQLite 写操作重试策略。
/// 使用 Polly 指数退避处理 SQLITE_BUSY 错误。
/// </summary>
public static class DatabaseRetryPolicy
{
    /// <summary>
    /// 创建 SQLite 写操作重试管道。
    /// 3次重试，延迟 100ms → 200ms → 400ms（指数退避）。
    /// 仅处理 SqliteException 且 SqliteErrorCode == 5 (SQLITE_BUSY)。
    /// </summary>
    public static ResiliencePipeline CreateRetryPipeline(ILogger? logger = null)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(100),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = false,
                ShouldHandle = new PredicateBuilder()
                    .Handle<SqliteException>(ex => ex.SqliteErrorCode == 5),
                OnRetry = args =>
                {
                    logger?.LogWarning(
                        "Database retry attempt {AttemptNumber} after {Delay}ms due to SQLITE_BUSY.",
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }
}
