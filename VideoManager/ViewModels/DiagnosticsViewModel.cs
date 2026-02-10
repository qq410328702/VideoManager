using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VideoManager.Data;
using VideoManager.Services;

namespace VideoManager.ViewModels;

/// <summary>
/// ViewModel for the Diagnostics view that displays application performance metrics,
/// memory usage, cache statistics, database information, and backup management.
/// Automatically refreshes metrics every 5 seconds using a DispatcherTimer.
/// </summary>
public partial class DiagnosticsViewModel : ViewModelBase, IDisposable
{
    private readonly IMetricsService _metricsService;
    private readonly IBackupService _backupService;
    private readonly IDbContextFactory<VideoManagerDbContext> _dbContextFactory;
    private readonly ILogger<DiagnosticsViewModel> _logger;
    private readonly DispatcherTimer _refreshTimer;
    private bool _disposed;

    /// <summary>
    /// Current managed heap memory usage in megabytes.
    /// </summary>
    [ObservableProperty]
    private long _managedMemoryMb;

    /// <summary>
    /// Current number of entries in the thumbnail cache.
    /// </summary>
    [ObservableProperty]
    private int _cacheCount;

    /// <summary>
    /// Thumbnail cache hit rate as a percentage (0.0 to 100.0).
    /// </summary>
    [ObservableProperty]
    private double _cacheHitRate;

    /// <summary>
    /// Average import operation time formatted as a human-readable string.
    /// </summary>
    [ObservableProperty]
    private string _avgImportTime = "N/A";

    /// <summary>
    /// Average search operation time formatted as a human-readable string.
    /// </summary>
    [ObservableProperty]
    private string _avgSearchTime = "N/A";

    /// <summary>
    /// Database file size in megabytes.
    /// </summary>
    [ObservableProperty]
    private long _dbFileSizeMb;

    /// <summary>
    /// Last backup time formatted as a human-readable string.
    /// </summary>
    [ObservableProperty]
    private string _lastBackupTime = "N/A";

    /// <summary>
    /// Collection of available backup files.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<BackupInfo> _backups = new();

    /// <summary>
    /// Creates a new DiagnosticsViewModel with injected services.
    /// Starts a DispatcherTimer that refreshes metrics every 5 seconds.
    /// </summary>
    /// <param name="metricsService">Service for collecting performance metrics.</param>
    /// <param name="backupService">Service for managing database backups.</param>
    /// <param name="dbContextFactory">Factory for creating database context instances to resolve the DB path.</param>
    /// <param name="logger">Logger for structured logging output.</param>
    public DiagnosticsViewModel(
        IMetricsService metricsService,
        IBackupService backupService,
        IDbContextFactory<VideoManagerDbContext> dbContextFactory,
        ILogger<DiagnosticsViewModel> logger)
    {
        _metricsService = metricsService ?? throw new ArgumentNullException(nameof(metricsService));
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();

        _logger.LogDebug("DiagnosticsViewModel initialized with 5-second auto-refresh");
    }

    /// <summary>
    /// Refreshes all diagnostic metrics from the MetricsService and BackupService.
    /// Reads memory usage, cache statistics, operation timings, database file size,
    /// and backup information.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            // Memory metrics
            ManagedMemoryMb = _metricsService.ManagedMemoryBytes / (1024 * 1024);

            // Cache metrics
            CacheCount = _metricsService.ThumbnailCacheCount;
            CacheHitRate = _metricsService.ThumbnailCacheHitRate * 100.0;

            // Operation timing metrics
            var avgImport = _metricsService.GetAverageTime("import");
            AvgImportTime = avgImport == TimeSpan.Zero ? "N/A" : FormatTimeSpan(avgImport);

            var avgSearch = _metricsService.GetAverageTime("search");
            AvgSearchTime = avgSearch == TimeSpan.Zero ? "N/A" : FormatTimeSpan(avgSearch);

            // Database file size
            DbFileSizeMb = await GetDatabaseFileSizeMbAsync();

            // Backup information
            var backupList = _backupService.ListBackups();
            Backups = new ObservableCollection<BackupInfo>(backupList);

            if (backupList.Count > 0)
            {
                LastBackupTime = backupList[0].CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
            }
            else
            {
                LastBackupTime = "N/A";
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error refreshing diagnostics metrics");
        }
    }

    /// <summary>
    /// Restores the database from the specified backup file.
    /// Delegates to <see cref="IBackupService.RestoreFromBackupAsync"/>.
    /// </summary>
    /// <param name="backup">The backup to restore from.</param>
    [RelayCommand]
    private async Task RestoreBackupAsync(BackupInfo? backup)
    {
        if (backup is null) return;

        try
        {
            _logger.LogInformation("Restoring database from backup: {BackupPath}", backup.FilePath);
            await _backupService.RestoreFromBackupAsync(backup.FilePath, CancellationToken.None);
            _logger.LogInformation("Database restored successfully from backup: {BackupPath}", backup.FilePath);

            // Refresh metrics after restore
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore database from backup: {BackupPath}", backup.FilePath);
            throw;
        }
    }

    /// <summary>
    /// Gets the database file size in megabytes by resolving the database path
    /// from the DbContext connection string.
    /// </summary>
    /// <returns>The database file size in MB, or 0 if the file cannot be found.</returns>
    private async Task<long> GetDatabaseFileSizeMbAsync()
    {
        try
        {
            var dbPath = await ResolveDatabasePathAsync();
            if (!string.IsNullOrEmpty(dbPath) && File.Exists(dbPath))
            {
                var fileInfo = new FileInfo(dbPath);
                return fileInfo.Length / (1024 * 1024);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error getting database file size");
        }

        return 0;
    }

    /// <summary>
    /// Resolves the database file path from the DbContext connection string.
    /// </summary>
    /// <returns>The full path to the database file, or null if it cannot be resolved.</returns>
    private async Task<string?> ResolveDatabasePathAsync()
    {
        try
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync();
            var connectionString = context.Database.GetConnectionString();
            if (!string.IsNullOrEmpty(connectionString))
            {
                var builder = new SqliteConnectionStringBuilder(connectionString);
                if (!string.IsNullOrEmpty(builder.DataSource))
                {
                    return Path.GetFullPath(builder.DataSource);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve database path from DbContext");
        }

        return null;
    }

    /// <summary>
    /// Formats a TimeSpan into a human-readable string.
    /// Shows milliseconds for durations under 1 second, otherwise shows seconds with one decimal.
    /// </summary>
    /// <param name="timeSpan">The time span to format.</param>
    /// <returns>A formatted string representation of the time span.</returns>
    internal static string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalMilliseconds < 1000)
        {
            return $"{timeSpan.TotalMilliseconds:F0} ms";
        }

        return $"{timeSpan.TotalSeconds:F1} s";
    }

    /// <summary>
    /// Stops the auto-refresh timer and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _refreshTimer.Stop();
        _logger.LogDebug("DiagnosticsViewModel disposed");
    }
}
