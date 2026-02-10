using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Repositories;
using VideoManager.Services;
using VideoManager.ViewModels;

namespace VideoManager.Tests.Unit;

/// <summary>
/// Unit tests for cancellation operations across Import, Search, and Delete services.
/// Validates Requirements 6.1, 6.2, 6.3.
/// </summary>
public class CancellationOperationTests
{
    #region Helpers

    private static Mock<IImportService> CreateImportServiceMock() => new();

    private static IOptions<VideoManagerOptions> CreateOptions() =>
        Options.Create(new VideoManagerOptions { VideoLibraryPath = "C:\\TestLibrary" });

    private static ImportViewModel CreateImportViewModel(Mock<IImportService> importServiceMock) =>
        new(importServiceMock.Object, CreateOptions());

    private static List<VideoFileInfo> CreateScanResult(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new VideoFileInfo($"/source/video{i}.mp4", $"video{i}.mp4", 1024 * i))
            .ToList();

    #endregion

    #region Requirement 6.1 — Import Cancellation Updates UI State to "已取消"

    [Fact]
    public async Task ImportCancellation_StatusMessage_ContainsCancelled()
    {
        // Arrange: set up an import that blocks until cancelled
        var importServiceMock = CreateImportServiceMock();
        var files = CreateScanResult(5);

        importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        var tcs = new TaskCompletionSource<ImportResult>();
        importServiceMock
            .Setup(s => s.ImportVideosAsync(
                It.IsAny<List<VideoFileInfo>>(),
                It.IsAny<ImportMode>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns((List<VideoFileInfo> _, ImportMode _, IProgress<ImportProgress> _, CancellationToken ct) =>
            {
                ct.Register(() => tcs.TrySetCanceled(ct));
                return tcs.Task;
            });

        var vm = CreateImportViewModel(importServiceMock);
        vm.SelectedFolderPath = "/test/folder";
        vm.LibraryPath = "/library";
        await vm.ScanFolderCommand.ExecuteAsync(null);

        // Act: start import then cancel
        var importTask = vm.ImportCommand.ExecuteAsync(null);
        Assert.True(vm.IsImporting);

        vm.CancelCommand.Execute(null);
        await importTask;

        // Assert: UI state reflects cancellation
        Assert.False(vm.IsImporting);
        Assert.Contains("取消", vm.StatusMessage);
    }

    [Fact]
    public async Task ImportCancellation_IsImporting_BecomesFalse()
    {
        // Arrange
        var importServiceMock = CreateImportServiceMock();
        var files = CreateScanResult(3);

        importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        var tcs = new TaskCompletionSource<ImportResult>();
        importServiceMock
            .Setup(s => s.ImportVideosAsync(
                It.IsAny<List<VideoFileInfo>>(),
                It.IsAny<ImportMode>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns((List<VideoFileInfo> _, ImportMode _, IProgress<ImportProgress> _, CancellationToken ct) =>
            {
                ct.Register(() => tcs.TrySetCanceled(ct));
                return tcs.Task;
            });

        var vm = CreateImportViewModel(importServiceMock);
        vm.SelectedFolderPath = "/test/folder";
        vm.LibraryPath = "/library";
        await vm.ScanFolderCommand.ExecuteAsync(null);

        // Act
        var importTask = vm.ImportCommand.ExecuteAsync(null);
        Assert.True(vm.IsImporting);

        vm.CancelCommand.Execute(null);
        await importTask;

        // Assert
        Assert.False(vm.IsImporting);
    }

    [Fact]
    public async Task ImportCancellation_EstimatedTimeRemaining_IsCleared()
    {
        // Arrange
        var importServiceMock = CreateImportServiceMock();
        var files = CreateScanResult(3);

        importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        var tcs = new TaskCompletionSource<ImportResult>();
        importServiceMock
            .Setup(s => s.ImportVideosAsync(
                It.IsAny<List<VideoFileInfo>>(),
                It.IsAny<ImportMode>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns((List<VideoFileInfo> _, ImportMode _, IProgress<ImportProgress> _, CancellationToken ct) =>
            {
                ct.Register(() => tcs.TrySetCanceled(ct));
                return tcs.Task;
            });

        var vm = CreateImportViewModel(importServiceMock);
        vm.SelectedFolderPath = "/test/folder";
        vm.LibraryPath = "/library";
        await vm.ScanFolderCommand.ExecuteAsync(null);

        // Act
        var importTask = vm.ImportCommand.ExecuteAsync(null);
        vm.CancelCommand.Execute(null);
        await importTask;

        // Assert: estimated time remaining is cleared after cancellation
        Assert.Equal(string.Empty, vm.EstimatedTimeRemaining);
    }

    [Fact]
    public async Task ImportCancellation_ImportResult_RemainsNull()
    {
        // Arrange: when import is cancelled, ImportResult should not be set
        var importServiceMock = CreateImportServiceMock();
        var files = CreateScanResult(2);

        importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        var tcs = new TaskCompletionSource<ImportResult>();
        importServiceMock
            .Setup(s => s.ImportVideosAsync(
                It.IsAny<List<VideoFileInfo>>(),
                It.IsAny<ImportMode>(),
                It.IsAny<IProgress<ImportProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns((List<VideoFileInfo> _, ImportMode _, IProgress<ImportProgress> _, CancellationToken ct) =>
            {
                ct.Register(() => tcs.TrySetCanceled(ct));
                return tcs.Task;
            });

        var vm = CreateImportViewModel(importServiceMock);
        vm.SelectedFolderPath = "/test/folder";
        vm.LibraryPath = "/library";
        await vm.ScanFolderCommand.ExecuteAsync(null);

        // Act
        var importTask = vm.ImportCommand.ExecuteAsync(null);
        vm.CancelCommand.Execute(null);
        await importTask;

        // Assert: no import result is set on cancellation
        Assert.Null(vm.ImportResult);
    }

    [Fact]
    public async Task ScanCancellation_StatusMessage_ContainsCancelled()
    {
        // Arrange: set up a scan that blocks until cancelled
        var importServiceMock = CreateImportServiceMock();

        var tcs = new TaskCompletionSource<List<VideoFileInfo>>();
        importServiceMock
            .Setup(s => s.ScanFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken ct) =>
            {
                ct.Register(() => tcs.TrySetCanceled(ct));
                return tcs.Task;
            });

        var vm = CreateImportViewModel(importServiceMock);
        vm.SelectedFolderPath = "/test/folder";

        // Act
        var scanTask = vm.ScanFolderCommand.ExecuteAsync(null);
        Assert.True(vm.IsScanning);

        vm.CancelCommand.Execute(null);
        await scanTask;

        // Assert
        Assert.False(vm.IsScanning);
        Assert.Contains("取消", vm.StatusMessage);
    }

    #endregion

    #region Requirement 6.2 — Search Cancellation Does Not Return Partial Results

    [Fact]
    public async Task SearchCancellation_ThrowsOperationCanceledException()
    {
        // Arrange: create an in-memory database with data
        var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var context = new VideoManagerDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();

        // Add some videos
        for (int i = 0; i < 10; i++)
        {
            context.VideoEntries.Add(new VideoEntry
            {
                Title = $"Test Video {i}",
                FileName = $"video{i}.mp4",
                FilePath = $"/videos/video{i}.mp4",
                FileSize = 1024,
                Duration = TimeSpan.FromMinutes(5),
                Width = 1920,
                Height = 1080,
                ImportedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
        }
        await context.SaveChangesAsync();

        var searchService = new SearchService(context, CreateNoOpMetricsService(), NullLogger<SearchService>.Instance);

        // Act & Assert: pre-cancelled token should throw OperationCanceledException
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var criteria = new SearchCriteria("Test", null, null, null, null, null);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            searchService.SearchAsync(criteria, 1, 10, cts.Token));
    }

    [Fact]
    public async Task SearchCancellation_NoFilters_ThrowsOperationCanceledException()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var context = new VideoManagerDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();

        for (int i = 0; i < 5; i++)
        {
            context.VideoEntries.Add(new VideoEntry
            {
                Title = $"Video {i}",
                FileName = $"video{i}.mp4",
                FilePath = $"/videos/video{i}.mp4",
                FileSize = 1024,
                Duration = TimeSpan.FromMinutes(5),
                Width = 1920,
                Height = 1080,
                ImportedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
        }
        await context.SaveChangesAsync();

        var searchService = new SearchService(context, CreateNoOpMetricsService(), NullLogger<SearchService>.Instance);

        // Act & Assert: pre-cancelled token should throw, not return partial results
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var criteria = new SearchCriteria(null, null, null, null, null, null);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            searchService.SearchAsync(criteria, 1, 10, cts.Token));
    }

    [Fact]
    public async Task SearchCancellation_DynamicQuery_ThrowsOperationCanceledException()
    {
        // Arrange: use a multi-condition query to force the dynamic LINQ path
        var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var context = new VideoManagerDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();

        var tag = new Tag { Name = "TestTag" };
        context.Tags.Add(tag);
        await context.SaveChangesAsync();

        for (int i = 0; i < 5; i++)
        {
            var video = new VideoEntry
            {
                Title = $"Video {i}",
                FileName = $"video{i}.mp4",
                FilePath = $"/videos/video{i}.mp4",
                FileSize = 1024,
                Duration = TimeSpan.FromMinutes(5),
                Width = 1920,
                Height = 1080,
                ImportedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            video.Tags.Add(tag);
            context.VideoEntries.Add(video);
        }
        await context.SaveChangesAsync();

        var searchService = new SearchService(context, CreateNoOpMetricsService(), NullLogger<SearchService>.Instance);

        // Act & Assert: multi-condition query with cancelled token
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var criteria = new SearchCriteria("Video", new List<int> { tag.Id }, null, null, null, null);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            searchService.SearchAsync(criteria, 1, 10, cts.Token));
    }

    #endregion

    #region Requirement 6.3 — Delete Cancellation via CancellationToken

    [Fact]
    public async Task BatchDeleteCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var context = new VideoManagerDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();

        var videoIds = new List<int>();
        for (int i = 0; i < 5; i++)
        {
            var video = new VideoEntry
            {
                Title = $"Video {i}",
                FileName = $"video{i}.mp4",
                FilePath = $"/videos/video{i}.mp4",
                FileSize = 1024,
                Duration = TimeSpan.FromMinutes(5),
                Width = 1920,
                Height = 1080,
                ImportedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            context.VideoEntries.Add(video);
            await context.SaveChangesAsync();
            videoIds.Add(video.Id);
        }

        var deleteService = new DeleteService(context, NullLogger<DeleteService>.Instance);

        // Act & Assert: pre-cancelled token should throw
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            deleteService.BatchDeleteAsync(videoIds, deleteFiles: false, progress: null, cts.Token));
    }

    [Fact]
    public async Task BatchDeleteCancellation_PreservesPreviouslyDeletedItems()
    {
        // Arrange: cancel after first item is processed
        var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var context = new VideoManagerDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();

        var videoIds = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            var video = new VideoEntry
            {
                Title = $"Video {i}",
                FileName = $"video{i}.mp4",
                FilePath = $"/videos/video{i}.mp4",
                FileSize = 1024,
                Duration = TimeSpan.FromMinutes(5),
                Width = 1920,
                Height = 1080,
                ImportedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            context.VideoEntries.Add(video);
            await context.SaveChangesAsync();
            videoIds.Add(video.Id);
        }

        var deleteService = new DeleteService(context, NullLogger<DeleteService>.Instance);

        // Use a CancellationTokenSource that cancels after the first progress report
        using var cts = new CancellationTokenSource();
        var progress = new Progress<BatchProgress>(p =>
        {
            if (p.Completed >= 1)
                cts.Cancel();
        });

        // Act: batch delete should throw after processing at least one item
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            deleteService.BatchDeleteAsync(videoIds, deleteFiles: false, progress, cts.Token));

        // Assert: the first video should be soft-deleted (IsDeleted=true)
        // Must use IgnoreQueryFilters because the global filter excludes soft-deleted records
        var firstVideo = await context.VideoEntries
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.Id == videoIds[0]);
        Assert.NotNull(firstVideo);
        Assert.True(firstVideo.IsDeleted); // Soft-deleted

        // At least one remaining video should not be deleted
        var remainingNotDeleted = await context.VideoEntries
            .CountAsync();
        Assert.True(remainingNotDeleted > 0, "At least one video should remain undeleted after cancellation");
    }

    [Fact]
    public async Task SingleDeleteCancellation_ThrowsOperationCanceledException()
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
            Title = "Test Video",
            FileName = "test.mp4",
            FilePath = "/videos/test.mp4",
            FileSize = 1024,
            Duration = TimeSpan.FromMinutes(5),
            Width = 1920,
            Height = 1080,
            ImportedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        context.VideoEntries.Add(video);
        await context.SaveChangesAsync();

        var deleteService = new DeleteService(context, NullLogger<DeleteService>.Instance);

        // Act & Assert: pre-cancelled token should throw
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            deleteService.DeleteVideoAsync(video.Id, deleteFile: false, cts.Token));
    }

    [Fact]
    public void BatchDeleteCancellation_VideoListViewModel_EndsBatchOperation()
    {
        // Arrange: test that VideoListViewModel properly handles batch cancellation
        var repoMock = new Mock<IVideoRepository>();
        var vm = new VideoListViewModel(repoMock.Object);

        var ct = vm.BeginBatchOperation();
        Assert.True(vm.IsBatchOperating);

        // Act: simulate cancellation
        vm.CancelBatchCommand.Execute(null);

        // Assert: token is cancelled
        Assert.True(ct.IsCancellationRequested);

        // Simulate the finally block that would run in MainWindow
        vm.EndBatchOperation();

        Assert.False(vm.IsBatchOperating);
        Assert.Equal(string.Empty, vm.BatchProgressText);
        Assert.Equal(0, vm.BatchProgressPercentage);
        Assert.Equal(string.Empty, vm.BatchEstimatedTimeRemaining);
    }

    #endregion

    private static IMetricsService CreateNoOpMetricsService()
    {
        var mock = new Mock<IMetricsService>();
        mock.Setup(m => m.StartTimer(It.IsAny<string>())).Returns(new NoOpDisposable());
        return mock.Object;
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
