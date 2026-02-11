using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using VideoManager.Models;

namespace VideoManager.Services;

/// <summary>
/// FFmpegService 的熔断器装饰器。
/// 使用 Polly CircuitBreakerStrategy 包装所有 FFmpeg 调用。
/// 30秒窗口内5次失败 → Open 60秒。
/// </summary>
public class ResilientFFmpegService : IFFmpegService
{
    private readonly IFFmpegService _inner;
    private readonly ResiliencePipeline _circuitBreakerPipeline;
    private readonly ILogger<ResilientFFmpegService> _logger;

    public ResilientFFmpegService(IFFmpegService inner, ILogger<ResilientFFmpegService> logger)
        : this(inner, logger, CreateDefaultPipeline(logger))
    {
    }

    /// <summary>
    /// Constructor that accepts a custom pipeline (for testing with short durations).
    /// </summary>
    internal ResilientFFmpegService(IFFmpegService inner, ILogger<ResilientFFmpegService> logger, ResiliencePipeline pipeline)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _circuitBreakerPipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public Task<bool> CheckAvailabilityAsync()
    {
        // Availability check is not wrapped by circuit breaker — it's a diagnostic call
        return _inner.CheckAvailabilityAsync();
    }

    public async Task<VideoMetadata> ExtractMetadataAsync(string videoPath, CancellationToken ct)
    {
        return await _circuitBreakerPipeline.ExecuteAsync(
            async token => await _inner.ExtractMetadataAsync(videoPath, token),
            ct);
    }

    public async Task<string> GenerateThumbnailAsync(string videoPath, string outputDir, CancellationToken ct)
    {
        return await _circuitBreakerPipeline.ExecuteAsync(
            async token => await _inner.GenerateThumbnailAsync(videoPath, outputDir, token),
            ct);
    }

    internal static ResiliencePipeline CreateDefaultPipeline(ILogger logger)
    {
        return new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 1.0,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(60),
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
}
