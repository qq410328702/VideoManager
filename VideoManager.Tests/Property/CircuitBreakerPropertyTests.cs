using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.CircuitBreaker;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.Property;

/// <summary>
/// Property-based tests for ResilientFFmpegService circuit breaker.
/// **Feature: video-manager-optimization-v4, Property 6: 熔断器连续失败后开启**
/// **Validates: Requirements 3.2, 3.3**
///
/// For any sequence of 5 or more consecutive FFmpeg call failures (within a 30-second window),
/// subsequent calls should immediately fail (throw BrokenCircuitException) without actually
/// calling the inner FFmpegService.
/// </summary>
public class CircuitBreakerPropertyTests
{
    /// <summary>
    /// Creates a test circuit breaker pipeline with zero durations for fast testing.
    /// Same configuration as production but with minimal timing.
    /// </summary>
    private static ResiliencePipeline CreateTestPipeline()
    {
        return new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 1.0,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromMinutes(10), // Long break so it stays open during test
                ShouldHandle = new PredicateBuilder()
                    .Handle<Exception>(ex => ex is not OperationCanceledException),
            })
            .Build();
    }

    /// <summary>
    /// A stub IFFmpegService that tracks call counts and can be configured to throw.
    /// </summary>
    private class StubFFmpegService : IFFmpegService
    {
        public int ExtractMetadataCallCount { get; private set; }
        public int GenerateThumbnailCallCount { get; private set; }
        public bool ShouldThrow { get; set; }

        public Task<bool> CheckAvailabilityAsync() => Task.FromResult(true);

        public Task<VideoMetadata> ExtractMetadataAsync(string videoPath, CancellationToken ct)
        {
            ExtractMetadataCallCount++;
            if (ShouldThrow)
                throw new InvalidOperationException("FFmpeg failed");
            return Task.FromResult(new VideoMetadata(TimeSpan.FromSeconds(10), 1920, 1080, "h264", 5000000));
        }

        public Task<string> GenerateThumbnailAsync(string videoPath, string outputDir, CancellationToken ct)
        {
            GenerateThumbnailCallCount++;
            if (ShouldThrow)
                throw new InvalidOperationException("FFmpeg failed");
            return Task.FromResult("/path/to/thumb.jpg");
        }
    }

    /// <summary>
    /// Property: For any number of consecutive failures >= 5, the circuit breaker opens
    /// and subsequent calls throw BrokenCircuitException without calling the inner service.
    ///
    /// **Validates: Requirements 3.2, 3.3**
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property ConsecutiveFailures_OpensCircuit_SubsequentCallsFailImmediately()
    {
        // Generate failure counts from 5 to 15
        var failCountGen = FsCheck.Fluent.Gen.Choose(5, 15);
        // Generate additional calls after circuit opens (1 to 5)
        var extraCallsGen = FsCheck.Fluent.Gen.Choose(1, 5);

        return FsCheck.Fluent.Prop.ForAll(
            FsCheck.Fluent.Arb.From(failCountGen),
            FsCheck.Fluent.Arb.From(extraCallsGen),
            (failCount, extraCalls) =>
            {
                var stub = new StubFFmpegService { ShouldThrow = true };
                var pipeline = CreateTestPipeline();
                var service = new ResilientFFmpegService(
                    stub,
                    NullLogger<ResilientFFmpegService>.Instance,
                    pipeline);

                // Cause failCount consecutive failures to open the circuit
                for (var i = 0; i < failCount; i++)
                {
                    try
                    {
                        service.ExtractMetadataAsync("test.mp4", CancellationToken.None)
                            .GetAwaiter().GetResult();
                    }
                    catch (InvalidOperationException)
                    {
                        // Expected — inner service failure
                    }
                    catch (BrokenCircuitException)
                    {
                        // Circuit already opened from previous failures
                    }
                }

                // Record call count after initial failures
                var callCountAfterFailures = stub.ExtractMetadataCallCount;

                // Now make additional calls — they should all throw BrokenCircuitException
                // without calling the inner service
                var allBrokenCircuit = true;
                for (var i = 0; i < extraCalls; i++)
                {
                    try
                    {
                        service.ExtractMetadataAsync("test.mp4", CancellationToken.None)
                            .GetAwaiter().GetResult();
                        allBrokenCircuit = false; // Should not succeed
                    }
                    catch (BrokenCircuitException)
                    {
                        // Expected — circuit is open
                    }
                    catch (Exception)
                    {
                        allBrokenCircuit = false; // Wrong exception type
                    }
                }

                // Inner service should NOT have been called during the extra calls
                var noExtraInnerCalls = stub.ExtractMetadataCallCount == callCountAfterFailures;

                return allBrokenCircuit && noExtraInnerCalls;
            });
    }

    /// <summary>
    /// Property: For any number of consecutive failures >= 5 using GenerateThumbnailAsync,
    /// the circuit breaker opens and subsequent calls throw BrokenCircuitException.
    ///
    /// **Validates: Requirements 3.2, 3.3**
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property ConsecutiveFailures_GenerateThumbnail_OpensCircuit()
    {
        var failCountGen = FsCheck.Fluent.Gen.Choose(5, 15);
        var extraCallsGen = FsCheck.Fluent.Gen.Choose(1, 5);

        return FsCheck.Fluent.Prop.ForAll(
            FsCheck.Fluent.Arb.From(failCountGen),
            FsCheck.Fluent.Arb.From(extraCallsGen),
            (failCount, extraCalls) =>
            {
                var stub = new StubFFmpegService { ShouldThrow = true };
                var pipeline = CreateTestPipeline();
                var service = new ResilientFFmpegService(
                    stub,
                    NullLogger<ResilientFFmpegService>.Instance,
                    pipeline);

                // Cause failures to open the circuit
                for (var i = 0; i < failCount; i++)
                {
                    try
                    {
                        service.GenerateThumbnailAsync("test.mp4", "/out", CancellationToken.None)
                            .GetAwaiter().GetResult();
                    }
                    catch (InvalidOperationException) { }
                    catch (BrokenCircuitException) { }
                }

                var callCountAfterFailures = stub.GenerateThumbnailCallCount;

                // Additional calls should all throw BrokenCircuitException
                var allBrokenCircuit = true;
                for (var i = 0; i < extraCalls; i++)
                {
                    try
                    {
                        service.GenerateThumbnailAsync("test.mp4", "/out", CancellationToken.None)
                            .GetAwaiter().GetResult();
                        allBrokenCircuit = false;
                    }
                    catch (BrokenCircuitException) { }
                    catch (Exception) { allBrokenCircuit = false; }
                }

                var noExtraInnerCalls = stub.GenerateThumbnailCallCount == callCountAfterFailures;
                return allBrokenCircuit && noExtraInnerCalls;
            });
    }
}
