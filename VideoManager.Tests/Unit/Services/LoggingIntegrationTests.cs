using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Repositories;
using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

/// <summary>
/// Integration tests that verify key scenarios trigger the expected logging calls.
/// Uses Mock&lt;ILogger&lt;T&gt;&gt; to verify log invocations.
/// </summary>
public class LoggingIntegrationTests
{
    #region SearchService — Debug log on search execution

    [Fact]
    public async Task SearchService_SearchAsync_LogsDebugWithSearchDetails()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var context = new VideoManagerDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();

        var mockLogger = new Mock<ILogger<SearchService>>();
        var mockMetrics = new Mock<IMetricsService>();
        mockMetrics.Setup(m => m.StartTimer(It.IsAny<string>())).Returns(new NoOpDisposable());
        var service = new SearchService(context, mockMetrics.Object, mockLogger.Object);
        var criteria = new SearchCriteria(Keyword: "test", TagIds: null, DateFrom: null, DateTo: null,
            DurationMin: null, DurationMax: null);

        // Act
        await service.SearchAsync(criteria, page: 1, pageSize: 10, CancellationToken.None);

        // Assert — verify Debug-level log was invoked
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Search executed")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        context.Database.CloseConnection();
    }

    #endregion

    #region DeleteService — Information log on successful delete

    [Fact]
    public async Task DeleteService_DeleteVideoAsync_LogsInformationOnSuccess()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var context = new VideoManagerDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();

        var video = new VideoEntry
        {
            Title = "Log Test Video",
            FileName = "log_test.mp4",
            FilePath = "/videos/log_test.mp4",
            FileSize = 1024,
            Duration = TimeSpan.FromMinutes(1),
            Width = 1920,
            Height = 1080,
            ImportedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        context.VideoEntries.Add(video);
        await context.SaveChangesAsync();

        var mockLogger = new Mock<ILogger<DeleteService>>();
        var service = new DeleteService(context, mockLogger.Object);

        // Act
        await service.DeleteVideoAsync(video.Id, deleteFile: false, CancellationToken.None);

        // Assert — verify Information-level log was invoked for successful deletion
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Deleted video")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        context.Database.CloseConnection();
    }

    #endregion

    #region DeleteService — Warning log when video not found

    [Fact]
    public async Task DeleteService_DeleteVideoAsync_LogsWarningWhenVideoNotFound()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var context = new VideoManagerDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();

        var mockLogger = new Mock<ILogger<DeleteService>>();
        var service = new DeleteService(context, mockLogger.Object);

        // Act
        await service.DeleteVideoAsync(videoId: 999, deleteFile: false, CancellationToken.None);

        // Assert — verify Warning-level log was invoked for non-existent video
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("non-existent")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        context.Database.CloseConnection();
    }

    #endregion

    #region WindowSettingsService — Error log on JSON parse failure

    [Fact]
    public void WindowSettingsService_Load_LogsErrorOnInvalidJson()
    {
        // Arrange — create a temp file with invalid JSON
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "window-settings.json");
        File.WriteAllText(settingsPath, "{ invalid json content !!!");

        var mockLogger = new Mock<ILogger<WindowSettingsService>>();
        var service = new WindowSettingsService(
            settingsPath,
            () => new Rect(0, 0, 1920, 1080),
            mockLogger.Object);

        try
        {
            // Act
            var result = service.Load();

            // Assert — should return null and log Error
            Assert.Null(result);
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to parse")),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    #endregion

    #region ThumbnailCacheService — Debug log for cache hit and miss

    [Fact]
    public async Task ThumbnailCacheService_LoadThumbnailAsync_LogsDebugOnCacheMiss()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ThumbnailCacheService>>();
        var service = new ThumbnailCacheService(_ => true, Options.Create(new VideoManagerOptions()), mockLogger.Object);

        // Act — first call is a cache miss
        await service.LoadThumbnailAsync("/thumbnails/video1.jpg");

        // Assert — verify Debug-level log for cache miss
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("cache miss")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ThumbnailCacheService_LoadThumbnailAsync_LogsDebugOnCacheHit()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ThumbnailCacheService>>();
        var service = new ThumbnailCacheService(_ => true, Options.Create(new VideoManagerOptions()), mockLogger.Object);

        // Act — first call populates cache, second call is a hit
        await service.LoadThumbnailAsync("/thumbnails/video1.jpg");
        await service.LoadThumbnailAsync("/thumbnails/video1.jpg");

        // Assert — verify Debug-level log for cache hit (second call)
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("cache hit")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}