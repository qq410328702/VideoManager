using VideoManager.Models;

namespace VideoManager.Services;

public interface IFFmpegService
{
    Task<bool> CheckAvailabilityAsync();
    Task<VideoMetadata> ExtractMetadataAsync(string videoPath, CancellationToken ct);
    Task<string> GenerateThumbnailAsync(string videoPath, string outputDir, CancellationToken ct);
}

