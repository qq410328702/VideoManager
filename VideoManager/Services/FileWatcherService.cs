using System.IO;
using Microsoft.Extensions.Logging;

namespace VideoManager.Services;

/// <summary>
/// Implementation of <see cref="IFileWatcherService"/> that uses <see cref="FileSystemWatcher"/>
/// to monitor a directory for file deletions and renames.
/// If initialization fails, the service degrades gracefully by logging the error
/// and continuing without file monitoring (Requirement 15.4).
/// </summary>
public class FileWatcherService : IFileWatcherService
{
    private readonly ILogger<FileWatcherService> _logger;
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    /// <summary>
    /// Creates a new FileWatcherService with the specified logger.
    /// </summary>
    public FileWatcherService(ILogger<FileWatcherService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public event EventHandler<FileDeletedEventArgs>? FileDeleted;

    /// <inheritdoc />
    public event EventHandler<FileRenamedEventArgs>? FileRenamed;

    /// <inheritdoc />
    public void StartWatching(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            _logger.LogWarning("Directory path is null or empty. File watching is disabled.");
            return;
        }

        try
        {
            // Stop any existing watcher before starting a new one
            StopWatching();

            if (!Directory.Exists(directoryPath))
            {
                _logger.LogWarning("Directory does not exist: '{DirectoryPath}'. File watching is disabled.", directoryPath);
                return;
            }

            _watcher = new FileSystemWatcher(directoryPath)
            {
                NotifyFilter = NotifyFilters.FileName,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _watcher.Deleted += OnFileDeleted;
            _watcher.Renamed += OnFileRenamed;
            _watcher.Error += OnWatcherError;

            _logger.LogInformation("Started watching directory: '{DirectoryPath}'.", directoryPath);
        }
        catch (Exception ex)
        {
            // Requirement 15.4: Log error and continue normally; file monitoring degrades
            _logger.LogError(ex, "Failed to initialize FileSystemWatcher for '{DirectoryPath}'.", directoryPath);
            CleanupWatcher();
        }
    }

    /// <inheritdoc />
    public void StopWatching()
    {
        CleanupWatcher();
    }

    /// <summary>
    /// Handles the <see cref="FileSystemWatcher.Deleted"/> event.
    /// </summary>
    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        try
        {
            FileDeleted?.Invoke(this, new FileDeletedEventArgs(e.FullPath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file deleted event for '{FilePath}'.", e.FullPath);
        }
    }

    /// <summary>
    /// Handles the <see cref="FileSystemWatcher.Renamed"/> event.
    /// </summary>
    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        try
        {
            FileRenamed?.Invoke(this, new FileRenamedEventArgs(e.OldFullPath, e.FullPath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file renamed event for '{OldPath}' -> '{NewPath}'.", e.OldFullPath, e.FullPath);
        }
    }

    /// <summary>
    /// Handles the <see cref="FileSystemWatcher.Error"/> event (e.g., buffer overflow).
    /// </summary>
    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        var ex = e.GetException();
        _logger.LogError(ex, "FileSystemWatcher error.");
    }

    /// <summary>
    /// Safely disposes and nullifies the internal FileSystemWatcher.
    /// </summary>
    private void CleanupWatcher()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Deleted -= OnFileDeleted;
            _watcher.Renamed -= OnFileRenamed;
            _watcher.Error -= OnWatcherError;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases managed resources.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                CleanupWatcher();
            }

            _disposed = true;
        }
    }
}
