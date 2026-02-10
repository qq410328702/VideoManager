using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

public class MetricsServiceTests : IDisposable
{
    private readonly ILogger<MetricsService> _nullLogger = NullLogger<MetricsService>.Instance;
    private MetricsService? _service;

    public void Dispose()
    {
        _service?.Dispose();
    }

    #region Constructor and Initialization

    [Fact]
    public void Constructor_InitializesWithDefaultThreshold()
    {
        _service = new MetricsService(_nullLogger);

        Assert.Equal(512L * 1024 * 1024, _service.MemoryWarningThresholdBytes);
    }

    [Fact]
    public void Constructor_CollectsInitialMemoryBytes()
    {
        _service = new MetricsService(_nullLogger);

        Assert.True(_service.ManagedMemoryBytes > 0);
    }

    [Fact]
    public void Constructor_ThrowsOnNullLogger()
    {
        Assert.Throws<ArgumentNullException>(() => new MetricsService(null!));
    }

    #endregion

    #region ThumbnailCache Metrics

    [Fact]
    public void ThumbnailCacheCount_WithNullCacheService_ReturnsZero()
    {
        _service = new MetricsService(_nullLogger, thumbnailCacheService: null);

        Assert.Equal(0, _service.ThumbnailCacheCount);
    }

    [Fact]
    public void ThumbnailCacheHitRate_WithNullCacheService_ReturnsZero()
    {
        _service = new MetricsService(_nullLogger, thumbnailCacheService: null);

        Assert.Equal(0.0, _service.ThumbnailCacheHitRate);
    }

    [Fact]
    public void ThumbnailCacheCount_DelegatesToCacheService()
    {
        var mockCache = new Mock<IThumbnailCacheService>();
        mockCache.Setup(c => c.CacheCount).Returns(42);

        _service = new MetricsService(_nullLogger, mockCache.Object);

        Assert.Equal(42, _service.ThumbnailCacheCount);
    }

    [Fact]
    public void ThumbnailCacheHitRate_CalculatesCorrectly()
    {
        var mockCache = new Mock<IThumbnailCacheService>();
        mockCache.Setup(c => c.CacheHitCount).Returns(75);
        mockCache.Setup(c => c.CacheMissCount).Returns(25);

        _service = new MetricsService(_nullLogger, mockCache.Object);

        Assert.Equal(0.75, _service.ThumbnailCacheHitRate, precision: 2);
    }

    [Fact]
    public void ThumbnailCacheHitRate_ZeroAccesses_ReturnsZero()
    {
        var mockCache = new Mock<IThumbnailCacheService>();
        mockCache.Setup(c => c.CacheHitCount).Returns(0);
        mockCache.Setup(c => c.CacheMissCount).Returns(0);

        _service = new MetricsService(_nullLogger, mockCache.Object);

        Assert.Equal(0.0, _service.ThumbnailCacheHitRate);
    }

    #endregion

    #region StartTimer and RecordTiming

    [Fact]
    public void StartTimer_ReturnsNonNullDisposable()
    {
        _service = new MetricsService(_nullLogger);

        using var timer = _service.StartTimer("test_operation");

        Assert.NotNull(timer);
    }

    [Fact]
    public void StartTimer_ThrowsOnNullOperationName()
    {
        _service = new MetricsService(_nullLogger);

        Assert.Throws<ArgumentNullException>(() => _service.StartTimer(null!));
    }

    [Fact]
    public void StartTimer_RecordsTimingOnDispose()
    {
        _service = new MetricsService(_nullLogger);

        using (_service.StartTimer("test_op"))
        {
            Thread.Sleep(10); // Small delay to ensure non-zero timing
        }

        var lastTime = _service.GetLastTime("test_op");
        Assert.True(lastTime > TimeSpan.Zero);
    }

    [Fact]
    public void StartTimer_MultipleOperations_TrackedSeparately()
    {
        _service = new MetricsService(_nullLogger);

        using (_service.StartTimer("op_a"))
        {
            Thread.Sleep(10);
        }
        using (_service.StartTimer("op_b"))
        {
            Thread.Sleep(10);
        }

        Assert.True(_service.GetLastTime("op_a") > TimeSpan.Zero);
        Assert.True(_service.GetLastTime("op_b") > TimeSpan.Zero);
    }

    #endregion

    #region GetAverageTime

    [Fact]
    public void GetAverageTime_NoRecordings_ReturnsZero()
    {
        _service = new MetricsService(_nullLogger);

        Assert.Equal(TimeSpan.Zero, _service.GetAverageTime("nonexistent"));
    }

    [Fact]
    public void GetAverageTime_ThrowsOnNullOperationName()
    {
        _service = new MetricsService(_nullLogger);

        Assert.Throws<ArgumentNullException>(() => _service.GetAverageTime(null!));
    }

    [Fact]
    public void GetAverageTime_SingleRecording_ReturnsThatValue()
    {
        _service = new MetricsService(_nullLogger);
        var duration = TimeSpan.FromMilliseconds(100);

        _service.RecordTiming("test_op", duration);

        Assert.Equal(duration, _service.GetAverageTime("test_op"));
    }

    [Fact]
    public void GetAverageTime_MultipleRecordings_ReturnsAverage()
    {
        _service = new MetricsService(_nullLogger);

        _service.RecordTiming("test_op", TimeSpan.FromMilliseconds(100));
        _service.RecordTiming("test_op", TimeSpan.FromMilliseconds(200));
        _service.RecordTiming("test_op", TimeSpan.FromMilliseconds(300));

        var average = _service.GetAverageTime("test_op");
        Assert.Equal(TimeSpan.FromMilliseconds(200), average);
    }

    #endregion

    #region GetLastTime

    [Fact]
    public void GetLastTime_NoRecordings_ReturnsZero()
    {
        _service = new MetricsService(_nullLogger);

        Assert.Equal(TimeSpan.Zero, _service.GetLastTime("nonexistent"));
    }

    [Fact]
    public void GetLastTime_ThrowsOnNullOperationName()
    {
        _service = new MetricsService(_nullLogger);

        Assert.Throws<ArgumentNullException>(() => _service.GetLastTime(null!));
    }

    [Fact]
    public void GetLastTime_MultipleRecordings_ReturnsLast()
    {
        _service = new MetricsService(_nullLogger);

        _service.RecordTiming("test_op", TimeSpan.FromMilliseconds(100));
        _service.RecordTiming("test_op", TimeSpan.FromMilliseconds(200));
        _service.RecordTiming("test_op", TimeSpan.FromMilliseconds(300));

        Assert.Equal(TimeSpan.FromMilliseconds(300), _service.GetLastTime("test_op"));
    }

    #endregion

    #region MaxTimingEntries Eviction

    [Fact]
    public void RecordTiming_ExceedsMaxEntries_EvictsOldest()
    {
        _service = new MetricsService(_nullLogger);

        // Record MaxTimingEntriesPerOperation + 10 entries
        for (int i = 0; i < MetricsService.MaxTimingEntriesPerOperation + 10; i++)
        {
            _service.RecordTiming("test_op", TimeSpan.FromMilliseconds(i));
        }

        // The last entry should be the most recent one
        var lastTime = _service.GetLastTime("test_op");
        Assert.Equal(TimeSpan.FromMilliseconds(MetricsService.MaxTimingEntriesPerOperation + 9), lastTime);

        // The average should be based on the last MaxTimingEntriesPerOperation entries (10..109)
        var average = _service.GetAverageTime("test_op");
        // Average of 10, 11, 12, ..., 109 = (10 + 109) / 2 = 59.5
        var expectedAverage = TimeSpan.FromMilliseconds(59.5);
        Assert.Equal(expectedAverage.TotalMilliseconds, average.TotalMilliseconds, precision: 1);
    }

    #endregion

    #region MemoryWarningThresholdBytes

    [Fact]
    public void MemoryWarningThresholdBytes_CanBeSetAndRead()
    {
        _service = new MetricsService(_nullLogger);

        _service.MemoryWarningThresholdBytes = 1024L * 1024 * 256; // 256 MB

        Assert.Equal(256L * 1024 * 1024, _service.MemoryWarningThresholdBytes);
    }

    #endregion

    #region CheckMemoryUsage

    [Fact]
    public void CheckMemoryUsage_UpdatesManagedMemoryBytes()
    {
        _service = new MetricsService(_nullLogger);
        var initialMemory = _service.ManagedMemoryBytes;

        _service.CheckMemoryUsage();

        // Memory should still be a positive value after check
        Assert.True(_service.ManagedMemoryBytes > 0);
    }

    [Fact]
    public void CheckMemoryUsage_AboveThreshold_LogsWarning()
    {
        var mockLogger = new Mock<ILogger<MetricsService>>();
        _service = new MetricsService(mockLogger.Object);

        // Set threshold very low to trigger warning
        _service.MemoryWarningThresholdBytes = 1;

        _service.CheckMemoryUsage();

        // Verify warning was logged
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public void CheckMemoryUsage_BelowThreshold_DoesNotLogWarning()
    {
        var mockLogger = new Mock<ILogger<MetricsService>>();
        _service = new MetricsService(mockLogger.Object);

        // Set threshold very high so it won't be exceeded
        _service.MemoryWarningThresholdBytes = long.MaxValue;

        _service.CheckMemoryUsage();

        // Verify no warning was logged
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    #endregion

    #region Dispose

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        _service = new MetricsService(_nullLogger);

        _service.Dispose();
        _service.Dispose(); // Should not throw
    }

    #endregion
}
