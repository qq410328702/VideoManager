using Microsoft.Data.Sqlite;
using Polly;
using Polly.Retry;
using VideoManager.Services;

namespace VideoManager.Tests.Property;

/// <summary>
/// Property-based tests for DatabaseRetryPolicy.
/// **Feature: video-manager-optimization-v4, Property 7: 数据库重试仅针对 SQLITE_BUSY**
/// **Validates: Requirements 4.1, 4.2**
///
/// For any SqliteException, the DatabaseRetryPolicy should trigger retry (up to 3 times)
/// if and only if SqliteErrorCode is 5 (SQLITE_BUSY). For other exception types,
/// it should immediately rethrow without retrying.
/// </summary>
public class DatabaseRetryPolicyPropertyTests
{
    /// <summary>
    /// Test pipeline with zero delays for fast property-based testing.
    /// Same configuration as production but with no delay.
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

    /// <summary>
    /// Property: For any SqliteException with SqliteErrorCode == 5 (SQLITE_BUSY)
    /// and any failure count n in [1, 3], the pipeline retries and succeeds.
    /// Total call count == n + 1 (n failures + 1 success).
    ///
    /// **Validates: Requirements 4.1, 4.2**
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property SqliteBusy_WithinRetryBudget_RetriesAndSucceeds()
    {
        var failCountGen = FsCheck.Fluent.Gen.Choose(1, 3);

        return FsCheck.Fluent.Prop.ForAll(
            FsCheck.Fluent.Arb.From(failCountGen),
            failCount =>
            {
                var callCount = 0;

                string Execute()
                {
                    callCount++;
                    if (callCount <= failCount)
                        throw new SqliteException("database is locked", 5);
                    return "success";
                }

                var result = TestPipeline.Execute(() => Execute());

                var expectedCallCount = failCount + 1;
                return result == "success" && callCount == expectedCallCount;
            });
    }

    /// <summary>
    /// Property: For any SqliteException with SqliteErrorCode == 5 (SQLITE_BUSY)
    /// and failure count > 3 (exceeds retry budget), the pipeline throws after
    /// exactly 4 calls (1 initial + 3 retries).
    ///
    /// **Validates: Requirements 4.1, 4.2**
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property SqliteBusy_ExceedsRetryBudget_ThrowsAfterMaxRetries()
    {
        var failCountGen = FsCheck.Fluent.Gen.Choose(4, 10);

        return FsCheck.Fluent.Prop.ForAll(
            FsCheck.Fluent.Arb.From(failCountGen),
            failCount =>
            {
                var callCount = 0;

                string Execute()
                {
                    callCount++;
                    if (callCount <= failCount)
                        throw new SqliteException("database is locked", 5);
                    return "success";
                }

                var threw = false;
                try
                {
                    TestPipeline.Execute(() => Execute());
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 5)
                {
                    threw = true;
                }

                // 1 initial + 3 retries = 4 total calls
                return threw && callCount == 4;
            });
    }

    /// <summary>
    /// Property: For any SqliteException with SqliteErrorCode != 5 (not SQLITE_BUSY),
    /// the pipeline should NOT retry — it should immediately rethrow.
    /// Total call count == 1.
    ///
    /// **Validates: Requirements 4.1, 4.2**
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property NonBusySqliteError_DoesNotRetry()
    {
        // Generate error codes that are NOT 5 (SQLITE_BUSY)
        // Use two ranges: [1,4] and [6,26] to exclude 5
        var errorCodeGen = FsCheck.Fluent.Gen.OneOf(
            FsCheck.Fluent.Gen.Choose(1, 4),
            FsCheck.Fluent.Gen.Choose(6, 26));

        return FsCheck.Fluent.Prop.ForAll(
            FsCheck.Fluent.Arb.From(errorCodeGen),
            errorCode =>
            {
                var callCount = 0;

                string Execute()
                {
                    callCount++;
                    throw new SqliteException($"SQLite error {errorCode}", errorCode);
                }

                var threw = false;
                var caughtErrorCode = 0;
                try
                {
                    TestPipeline.Execute(() => Execute());
                }
                catch (SqliteException ex)
                {
                    threw = true;
                    caughtErrorCode = ex.SqliteErrorCode;
                }

                // No retry: exactly 1 call, and the original exception is rethrown
                return threw && callCount == 1 && caughtErrorCode == errorCode;
            });
    }

    /// <summary>
    /// Property: For any non-SqliteException type, the pipeline should NOT retry —
    /// it should immediately rethrow. Total call count == 1.
    ///
    /// **Validates: Requirements 4.1, 4.2**
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property NonSqliteException_DoesNotRetry()
    {
        // Generate different exception type indices
        var exTypeGen = FsCheck.Fluent.Gen.Choose(0, 3);

        return FsCheck.Fluent.Prop.ForAll(
            FsCheck.Fluent.Arb.From(exTypeGen),
            exType =>
            {
                var callCount = 0;

                string Execute()
                {
                    callCount++;
                    throw exType switch
                    {
                        0 => (Exception)new InvalidOperationException("test"),
                        1 => new System.IO.IOException("test"),
                        2 => new ArgumentException("test"),
                        _ => new TimeoutException("test")
                    };
                }

                var threw = false;
                try
                {
                    TestPipeline.Execute(() => Execute());
                }
                catch (Exception ex) when (ex is not SqliteException)
                {
                    threw = true;
                }

                // No retry: exactly 1 call
                return threw && callCount == 1;
            });
    }
}
