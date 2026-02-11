using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VideoManager.Data;

namespace VideoManager.Services;

/// <summary>
/// Implementation of <see cref="IFileWatcherService"/> that uses <see cref="FileSystemWatcher"/>
/// to monitor a directory for file deletions and renames.
/// If initialization fails, the service degrades gracefully by logging the error
/// and continuing without file monitoring (Requirement 15.4).
/// Also supports periodic compensation scanning to detect file system changes
/// that FileSystemWatcher may have missed (Requirements 5.1–5.6).
/// </summary>
public class FileWatcherService : IFileWatcherService
{
    private readonly ILogger<FileWatcherService> _logger;
    private readonly IDbContextFactory<VideoManagerDbContext> _dbContextFactory;
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _compensationTimer;
    private bool _disposed;

    /// <summary>
    /// Creates a new FileWatcherService with the specified logger and DbContext factory.
    /// </summary>
    public FileWatcherService(
        ILogger<FileWatcherService> logger,
        IDbContextFactory<VideoManagerDbContext> dbContextFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    /// <inheritdoc />
    public event EventHandler<FileDeletedEventArgs>? FileDeleted;

    /// <inheritdoc />
    public event EventHandler<FileRenamedEventArgs>? FileRenamed;

    /// <inheritdoc />
    public event EventHandler<FilesMissingEventArgs>? FilesMissing;

    /// <inheritdoc />
    public event EventHandler<FilesRestoredEventArgs>? FilesRestored;

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

    /// <inheritdoc />
    public void StartCompensationScan(double scanIntervalHours = 1.0)
    {
        if (scanIntervalHours <= 0)
        {
            _logger.LogWarning("Compensation scan interval must be positive. Scan not started.");
            return;
        }

        StopCompensationScan();

        var interval = TimeSpan.FromHours(scanIntervalHours);
        _compensationTimer = new System.Threading.Timer(
            OnCompensationTimerCallback,
            null,
            interval,
            interval);

        _logger.LogInformation(
            "Compensation scan started with interval of {IntervalHours} hour(s).",
            scanIntervalHours);
    }

    /// <inheritdoc />
    public void StopCompensationScan()
    {
        if (_compensationTimer is not null)
        {
            _compensationTimer.Dispose();
            _compensationTimer = null;
            _logger.LogInformation("Compensation scan stopped.");
        }
    }

    /// <summary>
    /// Timer callback that triggers the compensation scan.
    /// Catches all exceptions to prevent the timer from stopping.
    /// </summary>
    private async void OnCompensationTimerCallback(object? state)
    {
        try
        {
            await ExecuteCompensationScanAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in compensation scan timer callback.");
        }
    }

    /// <summary>
    /// Executes a full compensation scan: queries all video records from the database,
    /// checks file existence on disk, and fires FilesMissing/FilesRestored events
    /// for any discrepancies.
    /// </summary>
    internal async Task ExecuteCompensationScanAsync()
    {
        _logger.LogDebug("Compensation scan starting.");

        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

            // Query all non-deleted video entries with their FilePath and IsFileMissing status.
            // Note: IsFileMissing is [NotMapped], so we track missing state via a separate
            // lightweight query that only selects Id and FilePath.
            var videoRecords = await dbContext.VideoEntries
                .IgnoreQueryFilters()
                .Where(v => !v.IsDeleted)
                .Select(v => new { v.Id, v.FilePath })
                .ToListAsync();

            if (videoRecords.Count == 0)
            {
                _logger.LogDebug("Compensation scan completed: no video records in database.");
                return;
            }

            var missingFilePaths = new List<string>();
            var restoredFilePaths = new List<string>();

            // We need to track which files were previously known as missing.
            // Since IsFileMissing is [NotMapped], we check file existence and compare
            // with a separate tracking set. For simplicity, we re-check all files each scan
            // and use the _knownMissingFiles set to detect transitions.
            foreach (var record in videoRecords)
            {
                try
                {
                    var fileExists = File.Exists(record.FilePath);
                    var wasMissing = _knownMissingFiles.Contains(record.FilePath);

                    if (!fileExists && !wasMissing)
                    {
                        // File is missing and wasn't previously known as missing
                        missingFilePaths.Add(record.FilePath);
                        _knownMissingFiles.Add(record.FilePath);
                    }
                    else if (fileExists && wasMissing)
                    {
                        // File has been restored
                        restoredFilePaths.Add(record.FilePath);
                        _knownMissingFiles.Remove(record.FilePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error checking file existence for '{FilePath}'. Skipping.",
                        record.FilePath);
                }
            }

            if (missingFilePaths.Count > 0)
            {
                _logger.LogWarning(
                    "Compensation scan found {Count} missing file(s).",
                    missingFilePaths.Count);
                FilesMissing?.Invoke(this, new FilesMissingEventArgs(missingFilePaths));
            }

            if (restoredFilePaths.Count > 0)
            {
                _logger.LogInformation(
                    "Compensation scan found {Count} restored file(s).",
                    restoredFilePaths.Count);
                FilesRestored?.Invoke(this, new FilesRestoredEventArgs(restoredFilePaths));
            }

            _logger.LogDebug(
                "Compensation scan completed: {Total} records checked, {Missing} missing, {Restored} restored.",
                videoRecords.Count, missingFilePaths.Count, restoredFilePaths.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Compensation scan failed. Will retry on next cycle.");
        }
    }

    /// <summary>
    /// Tracks file paths that are known to be missing from the file system.
    /// Used to detect transitions between missing and restored states across scans.
    /// </summary>
    private readonly HashSet<string> _knownMissingFiles = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Stop and dispose the compensation timer asynchronously
        if (_compensationTimer is not null)
        {
            await _compensationTimer.DisposeAsync().ConfigureAwait(false);
            _compensationTimer = null;
            _logger.LogInformation("Compensation scan stopped via async dispose.");
        }

        // Clean up the file system watcher (synchronous, no async API available)
        CleanupWatcher();

        _logger.LogInformation("FileWatcherService disposed asynchronously.");

        GC.SuppressFinalize(this);
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
                StopCompensationScan();
                CleanupWatcher();
            }

            _disposed = true;
        }
    }
}
