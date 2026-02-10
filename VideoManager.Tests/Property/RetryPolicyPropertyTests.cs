using System.IO;
using Polly;
using Polly.Retry;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for retry policy behavior.
/// **Feature: video-manager-optimization-v2, Property 3: 重试策略行为**
/// **Validates: Requirements 4.2, 4.3, 4.4**
///
/// For any failure count n (0 ≤ n ≤ 10), when a call fails n times then succeeds:
///   - If n ≤ 2, the final result should be the normal value (retry succeeded)
///   - If n > 2, the pipeline should throw (all retries exhausted), and we catch to return default
///   - Total call count should be min(n+1, 3)
/// </summary>
public class RetryPolicyPropertyTests
{
    /// <summary>
    /// Test-local resilience pipeline with the SAME configuration as ImportService.RetryPipeline
    /// but with zero delays for fast property-based testing.
    /// Config: MaxRetryAttempts = 2, linear backoff, excludes OperationCanceledException.
    /// </summary>
    private static readonly ResiliencePipeline TestRetryPipeline =
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.Zero, // No delay for fast tests
                BackoffType = DelayBackoffType.Linear,
                ShouldHandle = new PredicateBuilder()
                    .Handle<Exception>(ex => ex is not OperationCanceledException)
            })
            .Build();

    /// <summary>
    /// Normal result value returned on success.
    /// </summary>
    private const string NormalResult = "normal-metadata";

    /// <summary>
    /// Default result value used when all retries are exhausted.
    /// </summary>
    private const string DefaultResult = "default-metadata-zero-duration";

    /// <summary>
    /// Property: For any failure count n in [0, 10], when a function fails n times then succeeds:
    ///   - If n ≤ 2 (within retry budget), the pipeline returns the normal result
    ///   - If n > 2 (exceeds retry budget), the pipeline throws and we fall back to default
    ///   - Total call count == min(n+1, 3)
    ///
    /// **Validates: Requirements 4.2, 4.3, 4.4**
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property RetryPolicy_RespectsMaxAttemptsAndReturnsCorrectResult()
    {
        var failCountGen = FsCheck.Fluent.Gen.Choose(0, 10);

        return FsCheck.Fluent.Prop.ForAll(
            FsCheck.Fluent.Arb.From(failCountGen),
            failCount =>
            {
                var callCount = 0;

                // Simulate a function that fails failCount times then succeeds
                string CallWithFailures()
                {
                    callCount++;
                    if (callCount <= failCount)
                        throw new IOException($"Transient failure #{callCount}");
                    return NormalResult;
                }

                // Execute through the retry pipeline, catching exhausted retries
                string result;
                try
                {
                    result = TestRetryPipeline.Execute(() => CallWithFailures());
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // All retries exhausted — use default (mirrors ImportService behavior)
                    result = DefaultResult;
                }

                // Verify result correctness
                var expectedResult = failCount <= 2 ? NormalResult : DefaultResult;
                var resultCorrect = result == expectedResult;

                // Verify call count: min(n+1, 3)
                // n=0 → 1 call (immediate success)
                // n=1 → 2 calls (1 fail + 1 success)
                // n=2 → 3 calls (2 fails + 1 success)
                // n>2 → 3 calls (1 initial + 2 retries, all fail)
                var expectedCallCount = Math.Min(failCount + 1, 3);
                var callCountCorrect = callCount == expectedCallCount;

                return resultCorrect && callCountCorrect;
            });
    }

    /// <summary>
    /// Property: For any failure count n in [0, 10], the total number of invocations
    /// through the retry pipeline is exactly min(n+1, 3).
    ///
    /// This isolates the call-count invariant as a separate property for clearer diagnostics.
    ///
    /// **Validates: Requirements 4.2, 4.3, 4.4**
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property RetryPolicy_TotalCallCount_IsMinOfNPlus1And3()
    {
        var failCountGen = FsCheck.Fluent.Gen.Choose(0, 10);

        return FsCheck.Fluent.Prop.ForAll(
            FsCheck.Fluent.Arb.From(failCountGen),
            failCount =>
            {
                var callCount = 0;

                void CallWithFailures()
                {
                    callCount++;
                    if (callCount <= failCount)
                        throw new IOException($"Transient failure #{callCount}");
                }

                try
                {
                    TestRetryPipeline.Execute(() => CallWithFailures());
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Swallow — we only care about call count
                }

                var expectedCallCount = Math.Min(failCount + 1, 3);
                return callCount == expectedCallCount;
            });
    }

    /// <summary>
    /// Property: For any failure count n in [0, 2] (within retry budget),
    /// the pipeline should return the normal result (not throw).
    ///
    /// **Validates: Requirements 4.2, 4.3, 4.4**
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property RetryPolicy_WithinBudget_ReturnsNormalResult()
    {
        var failCountGen = FsCheck.Fluent.Gen.Choose(0, 2);

        return FsCheck.Fluent.Prop.ForAll(
            FsCheck.Fluent.Arb.From(failCountGen),
            failCount =>
            {
                var callCount = 0;

                string CallWithFailures()
                {
                    callCount++;
                    if (callCount <= failCount)
                        throw new IOException($"Transient failure #{callCount}");
                    return NormalResult;
                }

                // Should NOT throw when within retry budget
                var result = TestRetryPipeline.Execute(() => CallWithFailures());

                return result == NormalResult;
            });
    }

    /// <summary>
    /// Property: For any failure count n in [3, 10] (exceeds retry budget),
    /// the pipeline should throw (all retries exhausted).
    ///
    /// **Validates: Requirements 4.2, 4.3, 4.4**
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property RetryPolicy_ExceedsBudget_Throws()
    {
        var failCountGen = FsCheck.Fluent.Gen.Choose(3, 10);

        return FsCheck.Fluent.Prop.ForAll(
            FsCheck.Fluent.Arb.From(failCountGen),
            failCount =>
            {
                var callCount = 0;

                string CallWithFailures()
                {
                    callCount++;
                    if (callCount <= failCount)
                        throw new IOException($"Transient failure #{callCount}");
                    return NormalResult;
                }

                var threw = false;
                try
                {
                    TestRetryPipeline.Execute(() => CallWithFailures());
                }
                catch (IOException)
                {
                    threw = true;
                }

                return threw;
            });
    }
}
