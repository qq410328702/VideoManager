using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.CircuitBreaker;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

/// <summary>
/// Unit tests for ResilientFFmpegService.
/// Covers HalfOpen probe calls and state logging.
/// Requirements: 3.4, 3.5
/// </summary>
public class ResilientFFmpegServiceTests
{
    /// <summary>
    /// A stub IFFmpegService that can be configured to throw or succeed.
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
    /// Simple logger that captures log messages for verification.
    /// </summary>
    private class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    /// <summary>
    /// Creates a test pipeline with short break duration so we can test HalfOpen transitions.
    /// Polly requires BreakDuration >= 500ms.
    /// </summary>
    private static ResiliencePipeline CreateTestPipeline(ILogger logger, TimeSpan? breakDuration = null)
    {
        return new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 1.0,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = breakDuration ?? TimeSpan.FromMilliseconds(500),
                ShouldHandle = new PredicateBuilder()
                    .Handle<Exception>(ex => ex is not OperationCanceledException),
                OnOpened = args =>
                {
                    logger.LogWarning("FFmpeg circuit breaker opened. Duration: {BreakDuration}", args.BreakDuration);
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    logger.LogInformation("FFmpeg circuit breaker closed. Service recovered.");
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    logger.LogInformation("FFmpeg circuit breaker half-opened. Allowing probe call.");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    [Fact]
    public async Task HalfOpen_ProbeCallSucceeds_CircuitCloses()
    {
        var stub = new StubFFmpegService { ShouldThrow = true };
        var logger = new CapturingLogger<ResilientFFmpegService>();
        var pipeline = CreateTestPipeline(logger, TimeSpan.FromMilliseconds(500));
        var service = new ResilientFFmpegService(stub, logger, pipeline);

        // Cause enough failures to reliably open the circuit.
        // Use more than MinimumThroughput (5) to avoid timing edge cases
        // where the sampling window hasn't fully registered all failures.
        var circuitOpened = false;
        for (var i = 0; i < 10; i++)
        {
            try
            {
                await service.ExtractMetadataAsync("test.mp4", CancellationToken.None);
            }
            catch (BrokenCircuitException)
            {
                circuitOpened = true;
                break;
            }
            catch (InvalidOperationException) { }
        }

        Assert.True(circuitOpened, "Circuit breaker should have opened after repeated failures");

        // Wait for break duration to expire → HalfOpen
        await Task.Delay(700);

        // Now allow the probe call to succeed
        stub.ShouldThrow = false;

        // This probe call should succeed and close the circuit
        var result = await service.ExtractMetadataAsync("test.mp4", CancellationToken.None);
        Assert.NotNull(result);

        // Subsequent calls should also succeed (circuit is closed)
        var result2 = await service.ExtractMetadataAsync("test.mp4", CancellationToken.None);
        Assert.NotNull(result2);
    }

    [Fact]
    public async Task HalfOpen_ProbeCallFails_CircuitReopens()
    {
        var stub = new StubFFmpegService { ShouldThrow = true };
        var logger = new CapturingLogger<ResilientFFmpegService>();
        var pipeline = CreateTestPipeline(logger, TimeSpan.FromMilliseconds(500));
        var service = new ResilientFFmpegService(stub, logger, pipeline);

        // Cause 5 failures to open the circuit
        for (var i = 0; i < 5; i++)
        {
            try
            {
                await service.ExtractMetadataAsync("test.mp4", CancellationToken.None);
            }
            catch (InvalidOperationException) { }
            catch (BrokenCircuitException) { }
        }

        // Wait for break duration to expire → HalfOpen
        await Task.Delay(600);

        // Probe call fails (stub still throws)
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExtractMetadataAsync("test.mp4", CancellationToken.None));

        // Circuit should be open again — immediate BrokenCircuitException
        await Assert.ThrowsAsync<BrokenCircuitException>(
            () => service.ExtractMetadataAsync("test.mp4", CancellationToken.None));
    }

    [Fact]
    public async Task StateTransitions_AreLogged()
    {
        var stub = new StubFFmpegService { ShouldThrow = true };
        var logger = new CapturingLogger<ResilientFFmpegService>();
        var pipeline = CreateTestPipeline(logger, TimeSpan.FromMilliseconds(500));
        var service = new ResilientFFmpegService(stub, logger, pipeline);

        // Cause 5 failures to open the circuit
        for (var i = 0; i < 5; i++)
        {
            try
            {
                await service.ExtractMetadataAsync("test.mp4", CancellationToken.None);
            }
            catch (InvalidOperationException) { }
            catch (BrokenCircuitException) { }
        }

        // Verify "opened" was logged
        Assert.Contains(logger.Messages, m => m.Contains("opened"));

        // Wait for HalfOpen
        await Task.Delay(600);

        // Probe call succeeds → circuit closes
        stub.ShouldThrow = false;
        await service.ExtractMetadataAsync("test.mp4", CancellationToken.None);

        // Verify "half-opened" and "closed" were logged
        Assert.Contains(logger.Messages, m => m.Contains("half-opened"));
        Assert.Contains(logger.Messages, m => m.Contains("closed"));
    }

    [Fact]
    public async Task CheckAvailability_NotWrappedByCircuitBreaker()
    {
        var stub = new StubFFmpegService { ShouldThrow = true };
        var logger = new CapturingLogger<ResilientFFmpegService>();
        var pipeline = CreateTestPipeline(logger);
        var service = new ResilientFFmpegService(stub, logger, pipeline);

        // Cause 5 failures to open the circuit
        for (var i = 0; i < 5; i++)
        {
            try
            {
                await service.ExtractMetadataAsync("test.mp4", CancellationToken.None);
            }
            catch (InvalidOperationException) { }
            catch (BrokenCircuitException) { }
        }

        // CheckAvailability should still work even when circuit is open
        var available = await service.CheckAvailabilityAsync();
        Assert.True(available);
    }

    [Fact]
    public async Task OperationCanceledException_NotHandledByCircuitBreaker()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var stub = new StubFFmpegService();
        var logger = new CapturingLogger<ResilientFFmpegService>();
        var pipeline = CreateTestPipeline(logger, TimeSpan.FromMinutes(10));

        // Create a special stub that throws OperationCanceledException
        var cancelStub = new CancellingFFmpegService();
        var service = new ResilientFFmpegService(cancelStub, logger, pipeline);

        // OperationCanceledException should not count toward circuit breaker failures
        for (var i = 0; i < 10; i++)
        {
            try
            {
                await service.ExtractMetadataAsync("test.mp4", CancellationToken.None);
            }
            catch (OperationCanceledException) { }
        }

        // Circuit should NOT be open — no "opened" log message
        Assert.DoesNotContain(logger.Messages, m => m.Contains("opened"));
    }

    private class CancellingFFmpegService : IFFmpegService
    {
        public Task<bool> CheckAvailabilityAsync() => Task.FromResult(true);

        public Task<VideoMetadata> ExtractMetadataAsync(string videoPath, CancellationToken ct)
            => throw new OperationCanceledException("Cancelled");

        public Task<string> GenerateThumbnailAsync(string videoPath, string outputDir, CancellationToken ct)
            => throw new OperationCanceledException("Cancelled");
    }
}
