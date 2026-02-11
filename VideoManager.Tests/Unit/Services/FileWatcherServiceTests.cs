using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using VideoManager.Data;
using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

public class FileWatcherServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<IDbContextFactory<VideoManagerDbContext>> _dbContextFactoryMock;

    public FileWatcherServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FileWatcherTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbContextFactoryMock = new Mock<IDbContextFactory<VideoManagerDbContext>>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private FileWatcherService CreateService() =>
        new(NullLogger<FileWatcherService>.Instance, _dbContextFactoryMock.Object);

    #region StartWatching �?Initialization

    [Fact]
    public void StartWatching_ValidDirectory_DoesNotThrow()
    {
        using var service = CreateService();

        var ex = Record.Exception(() => service.StartWatching(_tempDir));

        Assert.Null(ex);
    }

    [Fact]
    public void StartWatching_NonExistentDirectory_DoesNotThrow()
    {
        // Requirement 15.4: graceful degradation
        using var service = CreateService();
        var nonExistent = Path.Combine(_tempDir, "does_not_exist");

        var ex = Record.Exception(() => service.StartWatching(nonExistent));

        Assert.Null(ex);
    }

    [Fact]
    public void StartWatching_NullPath_DoesNotThrow()
    {
        // Requirement 15.4: graceful degradation
        using var service = CreateService();

        var ex = Record.Exception(() => service.StartWatching(null!));

        Assert.Null(ex);
    }

    [Fact]
    public void StartWatching_EmptyPath_DoesNotThrow()
    {
        // Requirement 15.4: graceful degradation
        using var service = CreateService();

        var ex = Record.Exception(() => service.StartWatching(string.Empty));

        Assert.Null(ex);
    }

    [Fact]
    public void StartWatching_CalledTwice_DoesNotThrow()
    {
        using var service = CreateService();

        service.StartWatching(_tempDir);
        var ex = Record.Exception(() => service.StartWatching(_tempDir));

        Assert.Null(ex);
    }

    #endregion

    #region StopWatching

    [Fact]
    public void StopWatching_WithoutStarting_DoesNotThrow()
    {
        using var service = CreateService();

        var ex = Record.Exception(() => service.StopWatching());

        Assert.Null(ex);
    }

    [Fact]
    public void StopWatching_AfterStarting_DoesNotThrow()
    {
        using var service = CreateService();
        service.StartWatching(_tempDir);

        var ex = Record.Exception(() => service.StopWatching());

        Assert.Null(ex);
    }

    #endregion

    #region FileDeleted event

    [Fact]
    public async Task FileDeleted_WhenFileIsDeleted_RaisesEvent()
    {
        // Requirement 15.2: detect file deletion
        using var service = CreateService();
        var tcs = new TaskCompletionSource<FileDeletedEventArgs>();

        service.FileDeleted += (_, args) => tcs.TrySetResult(args);
        service.StartWatching(_tempDir);

        // Create and then delete a file
        var filePath = Path.Combine(_tempDir, "test_video.mp4");
        await File.WriteAllTextAsync(filePath, "dummy content");

        // Small delay to ensure watcher is ready
        await Task.Delay(100);

        File.Delete(filePath);

        // Wait for the event with timeout
        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(5000));

        Assert.Equal(tcs.Task, completedTask);
        var eventArgs = await tcs.Task;
        Assert.Equal(filePath, eventArgs.FilePath);
    }

    #endregion

    #region FileRenamed event

    [Fact]
    public async Task FileRenamed_WhenFileIsRenamed_RaisesEvent()
    {
        // Requirement 15.3: detect file rename
        using var service = CreateService();
        var tcs = new TaskCompletionSource<FileRenamedEventArgs>();

        service.FileRenamed += (_, args) => tcs.TrySetResult(args);
        service.StartWatching(_tempDir);

        // Create a file
        var oldPath = Path.Combine(_tempDir, "old_name.mp4");
        var newPath = Path.Combine(_tempDir, "new_name.mp4");
        await File.WriteAllTextAsync(oldPath, "dummy content");

        // Small delay to ensure watcher is ready
        await Task.Delay(100);

        File.Move(oldPath, newPath);

        // Wait for the event with timeout
        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(5000));

        Assert.Equal(tcs.Task, completedTask);
        var eventArgs = await tcs.Task;
        Assert.Equal(oldPath, eventArgs.OldPath);
        Assert.Equal(newPath, eventArgs.NewPath);
    }

    #endregion

    #region StopWatching �?Events no longer raised

    [Fact]
    public async Task StopWatching_AfterStop_NoEventsRaised()
    {
        using var service = CreateService();
        var eventRaised = false;

        service.FileDeleted += (_, _) => eventRaised = true;
        service.StartWatching(_tempDir);
        service.StopWatching();

        // Create and delete a file after stopping
        var filePath = Path.Combine(_tempDir, "test_after_stop.mp4");
        await File.WriteAllTextAsync(filePath, "dummy content");
        await Task.Delay(100);
        File.Delete(filePath);
        await Task.Delay(500);

        Assert.False(eventRaised);
    }

    #endregion

    #region Dispose

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        var service = CreateService();
        service.StartWatching(_tempDir);

        service.Dispose();
        var ex = Record.Exception(() => service.Dispose());

        Assert.Null(ex);
    }

    #endregion

    #region DisposeAsync — Requirements 9.2, 9.4

    [Fact]
    public async Task DisposeAsync_StopsCompensationScanAndFileMonitoring()
    {
        // Requirement 9.2: DisposeAsync should stop compensation scan and file monitoring
        var loggerMock = new Mock<ILogger<FileWatcherService>>();
        var service = new FileWatcherService(loggerMock.Object, _dbContextFactoryMock.Object);

        service.StartWatching(_tempDir);
        service.StartCompensationScan(1.0);

        await service.DisposeAsync();

        // Verify async dispose was logged
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("disposed asynchronously")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_CalledMultipleTimes_DoesNotThrow()
    {
        // Requirement 9.2: subsequent calls should be safe (idempotent)
        var service = CreateService();
        service.StartWatching(_tempDir);
        service.StartCompensationScan(1.0);

        await service.DisposeAsync();
        var ex = await Record.ExceptionAsync(async () => await service.DisposeAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task DisposeAsync_WithoutStarting_DoesNotThrow()
    {
        // DisposeAsync on a fresh service should be safe
        var service = CreateService();

        var ex = await Record.ExceptionAsync(async () => await service.DisposeAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task DisposeAsync_AfterDispose_DoesNotThrow()
    {
        // Requirement 9.4: both IDisposable and IAsyncDisposable coexist
        var service = CreateService();
        service.StartWatching(_tempDir);

        service.Dispose();
        var ex = await Record.ExceptionAsync(async () => await service.DisposeAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task Dispose_AfterDisposeAsync_DoesNotThrow()
    {
        // Requirement 9.4: both IDisposable and IAsyncDisposable coexist
        var service = CreateService();
        service.StartWatching(_tempDir);

        await service.DisposeAsync();
        var ex = Record.Exception(() => service.Dispose());

        Assert.Null(ex);
    }

    [Fact]
    public async Task DisposeAsync_ServiceImplementsIAsyncDisposable()
    {
        // Requirement 9.2: FileWatcherService SHALL implement IAsyncDisposable
        var service = CreateService();

        Assert.IsAssignableFrom<IAsyncDisposable>(service);

        await service.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_NoFileEventsRaisedAfterDispose()
    {
        // After DisposeAsync, file monitoring should be stopped
        var service = CreateService();
        var eventRaised = false;

        service.FileDeleted += (_, _) => eventRaised = true;
        service.StartWatching(_tempDir);

        await service.DisposeAsync();

        // Create and delete a file after disposing
        var filePath = Path.Combine(_tempDir, "test_after_dispose.mp4");
        await File.WriteAllTextAsync(filePath, "dummy content");
        await Task.Delay(200);
        if (File.Exists(filePath)) File.Delete(filePath);
        await Task.Delay(500);

        Assert.False(eventRaised);
    }

    #endregion

    #region Subdirectory monitoring

    [Fact]
    public async Task FileDeleted_InSubdirectory_RaisesEvent()
    {
        using var service = CreateService();
        var tcs = new TaskCompletionSource<FileDeletedEventArgs>();

        service.FileDeleted += (_, args) => tcs.TrySetResult(args);

        var subDir = Path.Combine(_tempDir, "subfolder");
        Directory.CreateDirectory(subDir);

        service.StartWatching(_tempDir);

        var filePath = Path.Combine(subDir, "sub_video.mp4");
        await File.WriteAllTextAsync(filePath, "dummy content");
        await Task.Delay(100);

        File.Delete(filePath);

        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(5000));

        Assert.Equal(tcs.Task, completedTask);
        var eventArgs = await tcs.Task;
        Assert.Equal(filePath, eventArgs.FilePath);
    }

    #endregion

    #region CompensationScan — Empty database

    [Fact]
    public async Task ExecuteCompensationScanAsync_EmptyDatabase_CompletesWithoutErrorsOrEvents()
    {
        // Requirement 5.5: scan should handle empty database gracefully
        var dbName = "EmptyDbTest_" + Guid.NewGuid().ToString("N");
        var connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        // Keep a connection open so the in-memory DB persists
        using var keepAlive = new SqliteConnection(connStr);
        keepAlive.Open();

        var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite(connStr)
            .Options;

        using (var ctx = new VideoManagerDbContext(options))
        {
            ctx.Database.EnsureCreated();
        }

        var factory = new Mock<IDbContextFactory<VideoManagerDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new VideoManagerDbContext(options));

        using var service = new FileWatcherService(NullLogger<FileWatcherService>.Instance, factory.Object);

        var missingFired = false;
        var restoredFired = false;
        service.FilesMissing += (_, _) => missingFired = true;
        service.FilesRestored += (_, _) => restoredFired = true;

        var ex = await Record.ExceptionAsync(() => service.ExecuteCompensationScanAsync());

        Assert.Null(ex);
        Assert.False(missingFired);
        Assert.False(restoredFired);
    }

    #endregion

    #region CompensationScan — Exception handling

    [Fact]
    public async Task ExecuteCompensationScanAsync_DbContextThrows_DoesNotCrashAndLogsError()
    {
        // Requirement 5.5: scan exception should be caught and logged, not crash
        var loggerMock = new Mock<ILogger<FileWatcherService>>();
        var factoryMock = new Mock<IDbContextFactory<VideoManagerDbContext>>();

        factoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated DB failure"));

        using var service = new FileWatcherService(loggerMock.Object, factoryMock.Object);

        var ex = await Record.ExceptionAsync(() => service.ExecuteCompensationScanAsync());

        // Should not throw — exception is caught internally
        Assert.Null(ex);

        // Verify error was logged
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Compensation scan failed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion
}
