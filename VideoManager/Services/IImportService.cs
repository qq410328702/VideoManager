using VideoManager.Models;

namespace VideoManager.Services;

/// <summary>
/// Service for scanning folders and importing video files into the Video Library.
/// </summary>
public interface IImportService
{
    /// <summary>
    /// Recursively scans a folder for supported video files (MP4, AVI, MKV, MOV, WMV).
    /// </summary>
    /// <param name="folderPath">The root folder path to scan.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of discovered video files with their metadata.</returns>
    Task<List<VideoFileInfo>> ScanFolderAsync(string folderPath, CancellationToken ct);

    /// <summary>
    /// Imports the specified video files into the Video Library.
    /// </summary>
    /// <param name="files">The video files to import.</param>
    /// <param name="mode">Whether to copy or move the files.</param>
    /// <param name="progress">Progress reporter for UI updates.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the import operation including success/failure counts.</returns>
    Task<ImportResult> ImportVideosAsync(List<VideoFileInfo> files, ImportMode mode, IProgress<ImportProgress> progress, CancellationToken ct);
}
