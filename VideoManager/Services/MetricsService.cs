using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace VideoManager.Services;

/// <summary>
/// Implementation of <see cref="IMetricsService"/> that collects performance metrics
/// including memory usage, operation timing, and thumbnail cache statistics.
/// Uses a <see cref="System.Threading.Timer"/> to periodically sample memory and cache metrics
/// every 5 seconds, and logs warnings when memory exceeds the configured threshold.
/// </summary>
public class MetricsService : IMetricsService
{
    /// <summary>
    /// Maximum number of timing entries to retain per operation.
    /// Older entries are removed when this limit is reached.
    /// </summary>
    internal const int MaxTimingEntriesPerOperation = 100;

    /// <summary>
    /// Interval in milliseconds between periodic metric collection cycles.
    /// </summary>
    internal const int CollectionIntervalMs = 5000;

    private readonly ILogger<MetricsService> _logger;
    private readonly IThumbnailCacheService? _thumbnailCacheService;
    private readonly ConcurrentDictionary<string, List<TimeSpan>> _operationTimings;
    private readonly object _timingsLock = new();
    private readonly Timer _collectionTimer;
    private long _managedMemoryBytes;
    private long _memoryWarningThresholdBytes;
    private bool _disposed;

    /// <summary>
    /// Creates a new MetricsService with the specified logger and optional thumbnail cache service.
    /// Starts periodic metric collection immediately.
    /// </summary>
    /// <param name="logger">Logger for structured logging output.</param>
    /// <param name="thumbnailCacheService">
    /// Optional thumbnail cache service for collecting cache metrics.
    /// May be null if the cache service is not available.
    /// </param>
    public MetricsService(ILogger<MetricsService> logger, IThumbnailCacheService? thumbnailCacheService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _thumbnailCacheService = thumbnailCacheService;
        _operationTimings = new ConcurrentDictionary<string, List<TimeSpan>>();
        _managedMemoryBytes = GC.GetTotalMemory(false);
        _memoryWarningThresholdBytes = 512L * 1024 * 1024; // Default 512 MB

        // Start periodic collection timer (every 5 seconds)
        _collectionTimer = new Timer(CollectMetrics, null, CollectionIntervalMs, CollectionIntervalMs);

        _logger.LogInformation("MetricsService initialized with collection interval: {IntervalMs}ms", CollectionIntervalMs);
    }

    /// <inheritdoc />
    public long ManagedMemoryBytes => Volatile.Read(ref _managedMemoryBytes);

    /// <inheritdoc />
    public int ThumbnailCacheCount => _thumbnailCacheService?.CacheCount ?? 0;

    /// <inheritdoc />
    public double ThumbnailCacheHitRate
    {
        get
        {
            if (_thumbnailCacheService == null)
                return 0.0;

            var hits = _thumbnailCacheService.CacheHitCount;
            var misses = _thumbnailCacheService.CacheMissCount;
            var total = hits + misses;
            return total == 0 ? 0.0 : (double)hits / total;
        }
    }

    /// <inheritdoc />
    public IDisposable StartTimer(string operationName)
    {
        ArgumentNullException.ThrowIfNull(operationName);
        return new OperationTimer(this, operationName);
    }

    /// <inheritdoc />
    public TimeSpan GetAverageTime(string operationName)
    {
        ArgumentNullException.ThrowIfNull(operationName);

        if (!_operationTimings.TryGetValue(operationName, out var timings))
            return TimeSpan.Zero;

        lock (_timingsLock)
        {
            if (timings.Count == 0)
                return TimeSpan.Zero;

            var totalTicks = 0L;
            foreach (var t in timings)
            {
                totalTicks += t.Ticks;
            }
            return TimeSpan.FromTicks(totalTicks / timings.Count);
        }
    }

    /// <inheritdoc />
    public TimeSpan GetLastTime(string operationName)
    {
        ArgumentNullException.ThrowIfNull(operationName);

        if (!_operationTimings.TryGetValue(operationName, out var timings))
            return TimeSpan.Zero;

        lock (_timingsLock)
        {
            return timings.Count == 0 ? TimeSpan.Zero : timings[^1];
        }
    }

    /// <inheritdoc />
    public long MemoryWarningThresholdBytes
    {
        get => Volatile.Read(ref _memoryWarningThresholdBytes);
        set => Volatile.Write(ref _memoryWarningThresholdBytes, value);
    }

    /// <inheritdoc />
    public void CheckMemoryUsage()
    {
        var currentMemory = GC.GetTotalMemory(false);
        Volatile.Write(ref _managedMemoryBytes, currentMemory);

        var threshold = MemoryWarningThresholdBytes;
        if (threshold > 0 && currentMemory > threshold)
        {
            _logger.LogWarning(
                "Memory usage {CurrentMemoryMb:F1} MB exceeds threshold {ThresholdMb:F1} MB. " +
                "Cache entries: {CacheCount}, Cache hit rate: {HitRate:P1}",
                currentMemory / (1024.0 * 1024.0),
                threshold / (1024.0 * 1024.0),
                ThumbnailCacheCount,
                ThumbnailCacheHitRate);
        }
    }

    /// <summary>
    /// Records a completed operation timing. Called by <see cref="OperationTimer"/> on disposal.
    /// Maintains a maximum of <see cref="MaxTimingEntriesPerOperation"/> entries per operation.
    /// </summary>
    internal void RecordTiming(string operationName, TimeSpan elapsed)
    {
        var timings = _operationTimings.GetOrAdd(operationName, _ => new List<TimeSpan>());

        lock (_timingsLock)
        {
            timings.Add(elapsed);

            // Keep only the most recent entries
            while (timings.Count > MaxTimingEntriesPerOperation)
            {
                timings.RemoveAt(0);
            }
        }

        _logger.LogDebug(
            "Operation {OperationName} completed in {ElapsedMs:F1}ms",
            operationName,
            elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// Timer callback that periodically collects memory and cache metrics.
    /// Exceptions are caught and logged to prevent timer from stopping.
    /// </summary>
    private void CollectMetrics(object? state)
    {
        try
        {
            CheckMemoryUsage();

            _logger.LogDebug(
                "Metrics collected - Memory: {MemoryMb:F1} MB, Cache entries: {CacheCount}, Hit rate: {HitRate:P1}",
                ManagedMemoryBytes / (1024.0 * 1024.0),
                ThumbnailCacheCount,
                ThumbnailCacheHitRate);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error collecting metrics");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _collectionTimer.Dispose();
        _logger.LogInformation("MetricsService disposed");
    }

    /// <summary>
    /// Internal disposable timer that measures the duration of an operation
    /// using <see cref="Stopwatch"/> and records it on disposal.
    /// </summary>
    private sealed class OperationTimer : IDisposable
    {
        private readonly MetricsService _metricsService;
        private readonly string _operationName;
        private readonly Stopwatch _stopwatch;
        private bool _disposed;

        public OperationTimer(MetricsService metricsService, string operationName)
        {
            _metricsService = metricsService;
            _operationName = operationName;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _stopwatch.Stop();
            _metricsService.RecordTiming(_operationName, _stopwatch.Elapsed);
        }
    }
}
