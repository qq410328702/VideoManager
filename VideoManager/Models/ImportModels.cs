namespace VideoManager.Models;

/// <summary>
/// Represents the mode used when importing video files.
/// </summary>
public enum ImportMode
{
    /// <summary>Copy the source file to the Video Library, leaving the original in place.</summary>
    Copy,

    /// <summary>Move the source file to the Video Library, removing the original.</summary>
    Move
}

/// <summary>
/// Information about a video file discovered during folder scanning.
/// </summary>
public record VideoFileInfo(string FilePath, string FileName, long FileSize);

/// <summary>
/// Progress information reported during a video import operation.
/// </summary>
public record ImportProgress(int Completed, int Total, string CurrentFile);

/// <summary>
/// The result of a video import operation.
/// </summary>
public record ImportResult(int SuccessCount, int FailCount, List<ImportError> Errors);

/// <summary>
/// Describes a single file that failed to import.
/// </summary>
public record ImportError(string FilePath, string Reason);
