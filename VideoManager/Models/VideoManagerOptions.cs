namespace VideoManager.Models;

/// <summary>
/// Configuration options for the Video Manager application.
/// Contains paths used for video library storage and thumbnail generation.
/// </summary>
/// <summary>
/// Configuration options for the Video Manager application.
/// Contains paths used for video library storage and thumbnail generation.
/// </summary>
public class VideoManagerOptions
{
    /// <summary>
    /// Path to the Video Library directory where imported video files are stored.
    /// </summary>
    public string VideoLibraryPath { get; set; } = string.Empty;

    /// <summary>
    /// Path to the directory where video thumbnails are stored.
    /// </summary>
    public string ThumbnailDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of entries in the thumbnail LRU cache.
    /// When the cache reaches this capacity, the least recently used entry is evicted.
    /// </summary>
    public int ThumbnailCacheMaxSize { get; set; } = 1000;

    /// <summary>
    /// Memory warning threshold in megabytes. When managed heap memory exceeds this value,
    /// the MetricsService logs a warning. Default is 512 MB.
    /// </summary>
    public long MemoryWarningThresholdMb { get; set; } = 512;

    /// <summary>
    /// Path to the directory where database backup files are stored.
    /// Defaults to empty string; when empty, the BackupService uses AppDir/Backups.
    /// </summary>
    public string BackupDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of backup files to retain. Older backups beyond this count
    /// are automatically deleted during cleanup. Default is 5.
    /// </summary>
    public int MaxBackupCount { get; set; } = 5;

    /// <summary>
    /// Interval in hours between automatic periodic backups. Default is 24 hours.
    /// </summary>
    public int BackupIntervalHours { get; set; } = 24;
}

