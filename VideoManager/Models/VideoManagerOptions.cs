namespace VideoManager.Models;

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
}
