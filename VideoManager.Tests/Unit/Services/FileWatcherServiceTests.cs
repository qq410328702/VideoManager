using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

public class FileWatcherServiceTests : IDisposable
{
    private readonly string _tempDir;

    public FileWatcherServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FileWatcherTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    #region StartWatching â€?Initialization

    [Fact]
    public void StartWatching_ValidDirectory_DoesNotThrow()
    {
        using var service = new FileWatcherService(NullLogger<FileWatcherService>.Instance);

        var ex = Record.Exception(() => service.StartWatching(_tempDir));

        Assert.Null(ex);
    }

    [Fact]
    public void StartWatching_NonExistentDirectory_DoesNotThrow()
    {
        // Requirement 15.4: graceful degradation
        using var service = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
        var nonExistent = Path.Combine(_tempDir, "does_not_exist");

        var ex = Record.Exception(() => service.StartWatching(nonExistent));

        Assert.Null(ex);
    }

    [Fact]
    public void StartWatching_NullPath_DoesNotThrow()
    {
        // Requirement 15.4: graceful degradation
        using var service = new FileWatcherService(NullLogger<FileWatcherService>.Instance);

        var ex = Record.Exception(() => service.StartWatching(null!));

        Assert.Null(ex);
    }

    [Fact]
    public void StartWatching_EmptyPath_DoesNotThrow()
    {
        // Requirement 15.4: graceful degradation
        using var service = new FileWatcherService(NullLogger<FileWatcherService>.Instance);

        var ex = Record.Exception(() => service.StartWatching(string.Empty));

        Assert.Null(ex);
    }

    [Fact]
    public void StartWatching_CalledTwice_DoesNotThrow()
    {
        using var service = new FileWatcherService(NullLogger<FileWatcherService>.Instance);

        service.StartWatching(_tempDir);
        var ex = Record.Exception(() => service.StartWatching(_tempDir));

        Assert.Null(ex);
    }

    #endregion

    #region StopWatching

    [Fact]
    public void StopWatching_WithoutStarting_DoesNotThrow()
    {
        using var service = new FileWatcherService(NullLogger<FileWatcherService>.Instance);

        var ex = Record.Exception(() => service.StopWatching());

        Assert.Null(ex);
    }

    [Fact]
    public void StopWatching_AfterStarting_DoesNotThrow()
    {
        using var service = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
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
        using var service = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
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
        using var service = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
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

    #region StopWatching â€?Events no longer raised

    [Fact]
    public async Task StopWatching_AfterStop_NoEventsRaised()
    {
        using var service = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
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
        var service = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
        service.StartWatching(_tempDir);

        service.Dispose();
        var ex = Record.Exception(() => service.Dispose());

        Assert.Null(ex);
    }

    #endregion

    #region Subdirectory monitoring

    [Fact]
    public async Task FileDeleted_InSubdirectory_RaisesEvent()
    {
        using var service = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
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
}
