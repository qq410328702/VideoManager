using Microsoft.Data.Sqlite;
using Polly;
using Polly.Retry;
using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

/// <summary>
/// Unit tests for DatabaseRetryPolicy.
/// Covers retry exhaustion scenarios and edge cases.
/// </summary>
public class DatabaseRetryPolicyTests
{
    /// <summary>
    /// Zero-delay pipeline for fast unit tests.
    /// </summary>
    private static readonly ResiliencePipeline TestPipeline =
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.Zero,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = false,
                ShouldHandle = new PredicateBuilder()
                    .Handle<SqliteException>(ex => ex.SqliteErrorCode == 5)
            })
            .Build();

    [Fact]
    public void CreateRetryPipeline_ReturnsNonNullPipeline()
    {
        var pipeline = DatabaseRetryPolicy.CreateRetryPipeline();
        Assert.NotNull(pipeline);
    }

    [Fact]
    public void CreateRetryPipeline_WithLogger_ReturnsNonNullPipeline()
    {
        var pipeline = DatabaseRetryPolicy.CreateRetryPipeline(
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        Assert.NotNull(pipeline);
    }

    [Fact]
    public void SqliteBusy_AllRetriesExhausted_ThrowsSqliteException()
    {
        var callCount = 0;

        var ex = Assert.Throws<SqliteException>(() =>
        {
            TestPipeline.Execute(() =>
            {
                callCount++;
                throw new SqliteException("database is locked", 5);
            });
        });

        Assert.Equal(5, ex.SqliteErrorCode);
        Assert.Equal(4, callCount); // 1 initial + 3 retries
    }

    [Fact]
    public void SqliteBusy_SucceedsOnLastRetry_ReturnsResult()
    {
        var callCount = 0;

        var result = TestPipeline.Execute(() =>
        {
            callCount++;
            if (callCount <= 3) // Fail 3 times, succeed on 4th (last retry)
                throw new SqliteException("database is locked", 5);
            return "success";
        });

        Assert.Equal("success", result);
        Assert.Equal(4, callCount);
    }

    [Fact]
    public void NonBusySqliteError_ImmediatelyThrows_NoRetry()
    {
        var callCount = 0;

        var ex = Assert.Throws<SqliteException>(() =>
        {
            TestPipeline.Execute(() =>
            {
                callCount++;
                throw new SqliteException("SQL logic error", 1); // SQLITE_ERROR
            });
        });

        Assert.Equal(1, ex.SqliteErrorCode);
        Assert.Equal(1, callCount); // No retry
    }

    [Fact]
    public void InvalidOperationException_ImmediatelyThrows_NoRetry()
    {
        var callCount = 0;

        Assert.Throws<InvalidOperationException>(() =>
        {
            TestPipeline.Execute(() =>
            {
                callCount++;
                throw new InvalidOperationException("test error");
            });
        });

        Assert.Equal(1, callCount); // No retry
    }

    [Fact]
    public void NoException_ExecutesOnce_ReturnsResult()
    {
        var callCount = 0;

        var result = TestPipeline.Execute(() =>
        {
            callCount++;
            return "success";
        });

        Assert.Equal("success", result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task SqliteBusy_AsyncExecution_AllRetriesExhausted_ThrowsSqliteException()
    {
        var callCount = 0;

        var ex = await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            await TestPipeline.ExecuteAsync(async _ =>
            {
                callCount++;
                await Task.CompletedTask;
                throw new SqliteException("database is locked", 5);
            }, CancellationToken.None);
        });

        Assert.Equal(5, ex.SqliteErrorCode);
        Assert.Equal(4, callCount); // 1 initial + 3 retries
    }

    [Fact]
    public async Task SqliteBusy_AsyncExecution_SucceedsAfterRetries()
    {
        var callCount = 0;

        await TestPipeline.ExecuteAsync(async _ =>
        {
            callCount++;
            await Task.CompletedTask;
            if (callCount <= 2)
                throw new SqliteException("database is locked", 5);
        }, CancellationToken.None);

        Assert.Equal(3, callCount); // 2 failures + 1 success
    }
}
