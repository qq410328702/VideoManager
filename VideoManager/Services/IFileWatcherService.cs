namespace VideoManager.Services;

/// <summary>
/// Service for monitoring file system changes in the video library directory.
/// Detects file deletions and renames, raising events so the application can
/// update video entries accordingly.
/// </summary>
public interface IFileWatcherService : IDisposable
{
    /// <summary>
    /// Starts monitoring the specified directory for file changes.
    /// Watches for deletions and renames of video files.
    /// </summary>
    /// <param name="directoryPath">The directory path to monitor.</param>
    void StartWatching(string directoryPath);

    /// <summary>
    /// Stops monitoring the directory and releases the underlying watcher resources.
    /// </summary>
    void StopWatching();

    /// <summary>
    /// Raised when a file is deleted from the monitored directory.
    /// </summary>
    event EventHandler<FileDeletedEventArgs> FileDeleted;

    /// <summary>
    /// Raised when a file is renamed in the monitored directory.
    /// </summary>
    event EventHandler<FileRenamedEventArgs> FileRenamed;
}

/// <summary>
/// Event arguments for a file deletion event.
/// </summary>
/// <param name="FilePath">The full path of the deleted file.</param>
public record FileDeletedEventArgs(string FilePath);

/// <summary>
/// Event arguments for a file rename event.
/// </summary>
/// <param name="OldPath">The original full path of the file before renaming.</param>
/// <param name="NewPath">The new full path of the file after renaming.</param>
public record FileRenamedEventArgs(string OldPath, string NewPath);
