namespace VideoManager.Services;

/// <summary>
/// Service for collecting and reporting application performance metrics.
/// Provides memory monitoring, operation timing, and cache statistics.
/// </summary>
public interface IMetricsService : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets the current managed heap memory usage in bytes.
    /// Collected periodically via <see cref="System.GC.GetTotalMemory(bool)"/>.
    /// </summary>
    long ManagedMemoryBytes { get; }

    /// <summary>
    /// Gets the current number of entries in the thumbnail cache.
    /// </summary>
    int ThumbnailCacheCount { get; }

    /// <summary>
    /// Gets the thumbnail cache hit rate as a value between 0.0 and 1.0.
    /// Returns 0.0 if no cache accesses have occurred.
    /// </summary>
    double ThumbnailCacheHitRate { get; }

    /// <summary>
    /// Starts a timer for the specified operation. The returned <see cref="IDisposable"/>
    /// records the elapsed time when disposed.
    /// Usage: <c>using (metricsService.StartTimer("import")) { /* operation */ }</c>
    /// </summary>
    /// <param name="operationName">The name of the operation to time.</param>
    /// <returns>An <see cref="IDisposable"/> that records the elapsed time on disposal.</returns>
    IDisposable StartTimer(string operationName);

    /// <summary>
    /// Gets the average recorded time for the specified operation.
    /// Returns <see cref="TimeSpan.Zero"/> if no times have been recorded.
    /// </summary>
    /// <param name="operationName">The name of the operation.</param>
    /// <returns>The average time across all recorded entries for the operation.</returns>
    TimeSpan GetAverageTime(string operationName);

    /// <summary>
    /// Gets the most recently recorded time for the specified operation.
    /// Returns <see cref="TimeSpan.Zero"/> if no times have been recorded.
    /// </summary>
    /// <param name="operationName">The name of the operation.</param>
    /// <returns>The last recorded time for the operation.</returns>
    TimeSpan GetLastTime(string operationName);

    /// <summary>
    /// Gets or sets the memory warning threshold in bytes.
    /// When <see cref="ManagedMemoryBytes"/> exceeds this value, a warning is logged.
    /// </summary>
    long MemoryWarningThresholdBytes { get; set; }

    /// <summary>
    /// Checks the current memory usage against <see cref="MemoryWarningThresholdBytes"/>
    /// and logs a warning if the threshold is exceeded.
    /// </summary>
    void CheckMemoryUsage();
}
