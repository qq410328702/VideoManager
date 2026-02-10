using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VideoManager.Data;

namespace VideoManager.Services;

/// <summary>
/// Implementation of <see cref="IDeleteService"/> that handles video deletion
/// from the database and optionally from the file system.
/// </summary>
public class DeleteService : IDeleteService
{
    private readonly VideoManagerDbContext _context;
    private readonly ILogger<DeleteService> _logger;

    public DeleteService(VideoManagerDbContext context, ILogger<DeleteService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<DeleteResult> DeleteVideoAsync(int videoId, bool deleteFile, CancellationToken ct)
    {
        var video = await _context.VideoEntries
            .Include(v => v.Tags)
            .Include(v => v.Categories)
            .FirstOrDefaultAsync(v => v.Id == videoId, ct);

        if (video is null)
        {
            _logger.LogWarning("Delete requested for non-existent video ID {VideoId}.", videoId);
            return new DeleteResult(false, $"Video with ID {videoId} was not found.");
        }

        string? fileError = null;

        if (deleteFile)
        {
            fileError = TryDeleteFiles(video.FilePath, video.ThumbnailPath);
        }

        // Soft delete: mark as deleted instead of physically removing (Req 6.3)
        video.Tags.Clear();
        video.Categories.Clear();
        video.IsDeleted = true;
        video.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted video '{Title}' (ID: {VideoId}), deleteFile={DeleteFile}.", video.Title, videoId, deleteFile);

        return new DeleteResult(true, fileError);
    }

    /// <inheritdoc />
    public async Task<BatchDeleteResult> BatchDeleteAsync(
        List<int> videoIds, bool deleteFiles,
        IProgress<BatchProgress>? progress, CancellationToken ct)
    {
        var successCount = 0;
        var failCount = 0;
        var errors = new List<DeleteError>();
        var total = videoIds.Count;

        for (var i = 0; i < videoIds.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var videoId = videoIds[i];
            try
            {
                var result = await DeleteVideoAsync(videoId, deleteFiles, ct);
                if (result.Success)
                {
                    successCount++;
                    if (result.ErrorMessage is not null)
                    {
                        // DB deletion succeeded but file deletion had issues
                        errors.Add(new DeleteError(videoId, result.ErrorMessage));
                    }
                }
                else
                {
                    failCount++;
                    errors.Add(new DeleteError(videoId, result.ErrorMessage ?? "Unknown error"));
                }
            }
            catch (Exception ex)
            {
                failCount++;
                errors.Add(new DeleteError(videoId, ex.Message));
            }

            progress?.Report(new BatchProgress(i + 1, total));
        }

        return new BatchDeleteResult(successCount, failCount, errors);
    }

    /// <summary>
    /// Attempts to delete the video file and thumbnail file from disk.
    /// Returns an error message if any deletion fails, or null if all succeeded.
    /// </summary>
    private string? TryDeleteFiles(string? filePath, string? thumbnailPath)
    {
        var errors = new List<string>();

        if (!string.IsNullOrEmpty(filePath))
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete video file '{FilePath}'.", filePath);
                errors.Add($"Failed to delete video file '{filePath}': {ex.Message}");
            }
        }

        if (!string.IsNullOrEmpty(thumbnailPath))
        {
            try
            {
                if (File.Exists(thumbnailPath))
                {
                    File.Delete(thumbnailPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete thumbnail '{ThumbnailPath}'.", thumbnailPath);
                errors.Add($"Failed to delete thumbnail '{thumbnailPath}': {ex.Message}");
            }
        }

        return errors.Count > 0 ? string.Join("; ", errors) : null;
    }
}
