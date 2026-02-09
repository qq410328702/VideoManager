namespace VideoManager.Services;

/// <summary>
/// Service for deleting video entries from the database and optionally from disk.
/// </summary>
public interface IDeleteService
{
    /// <summary>
    /// Deletes a single video entry from the database and its tag/category associations.
    /// When <paramref name="deleteFile"/> is true, also deletes the video file and thumbnail from disk.
    /// If file deletion fails, the database record is still removed and an error message is returned.
    /// </summary>
    /// <param name="videoId">The ID of the video to delete.</param>
    /// <param name="deleteFile">Whether to also delete the source video file and thumbnail.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result indicating success or containing an error message.</returns>
    Task<DeleteResult> DeleteVideoAsync(int videoId, bool deleteFile, CancellationToken ct);

    /// <summary>
    /// Deletes multiple video entries in batch, with optional progress reporting.
    /// Each video is processed independently; failure of one does not affect others.
    /// </summary>
    /// <param name="videoIds">The list of video IDs to delete.</param>
    /// <param name="deleteFiles">Whether to also delete source video files and thumbnails.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A summary of the batch delete operation.</returns>
    Task<BatchDeleteResult> BatchDeleteAsync(List<int> videoIds, bool deleteFiles, IProgress<BatchProgress>? progress, CancellationToken ct);
}

/// <summary>
/// Result of a single video delete operation.
/// </summary>
/// <param name="Success">Whether the database deletion succeeded.</param>
/// <param name="ErrorMessage">Error message if file deletion failed (database deletion still completes).</param>
public record DeleteResult(bool Success, string? ErrorMessage);

/// <summary>
/// Result of a batch delete operation.
/// </summary>
/// <param name="SuccessCount">Number of videos successfully deleted.</param>
/// <param name="FailCount">Number of videos that failed to delete.</param>
/// <param name="Errors">Details of each failure.</param>
public record BatchDeleteResult(int SuccessCount, int FailCount, List<DeleteError> Errors);

/// <summary>
/// Details of a single video deletion failure within a batch operation.
/// </summary>
/// <param name="VideoId">The ID of the video that failed.</param>
/// <param name="Reason">The reason for the failure.</param>
public record DeleteError(int VideoId, string Reason);

/// <summary>
/// Progress information for batch operations.
/// </summary>
/// <param name="Completed">Number of items processed so far.</param>
/// <param name="Total">Total number of items to process.</param>
public record BatchProgress(int Completed, int Total);
