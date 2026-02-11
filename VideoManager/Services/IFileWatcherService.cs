namespace VideoManager.Services;

/// <summary>
/// Service for monitoring file system changes in the video library directory.
/// Detects file deletions and renames, raising events so the application can
/// update video entries accordingly. Also supports periodic compensation scanning
/// to detect file system changes that FileSystemWatcher may have missed.
/// </summary>
public interface IFileWatcherService : IDisposable, IAsyncDisposable
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

    /// <summary>
    /// Starts periodic compensation scanning to detect file system changes
    /// that FileSystemWatcher may have missed.
    /// </summary>
    /// <param name="scanIntervalHours">Scan interval in hours, default 1.</param>
    void StartCompensationScan(double scanIntervalHours = 1.0);

    /// <summary>
    /// Stops the periodic compensation scan.
    /// </summary>
    void StopCompensationScan();

    /// <summary>
    /// Raised when compensation scan discovers files that exist in the database
    /// but are missing from the file system.
    /// </summary>
    event EventHandler<FilesMissingEventArgs>? FilesMissing;

    /// <summary>
    /// Raised when compensation scan discovers previously missing files
    /// that have reappeared in the file system.
    /// </summary>
    event EventHandler<FilesRestoredEventArgs>? FilesRestored;
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

/// <summary>
/// Event arguments for files missing from the file system (discovered by compensation scan).
/// </summary>
/// <param name="MissingFilePaths">The full paths of files that exist in the database but not on disk.</param>
public record FilesMissingEventArgs(IReadOnlyList<string> MissingFilePaths);

/// <summary>
/// Event arguments for previously missing files that have been restored (discovered by compensation scan).
/// </summary>
/// <param name="RestoredFilePaths">The full paths of files that were previously missing but now exist on disk.</param>
public record FilesRestoredEventArgs(IReadOnlyList<string> RestoredFilePaths);
