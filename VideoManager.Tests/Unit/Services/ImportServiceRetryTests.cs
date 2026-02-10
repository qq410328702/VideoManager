using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using VideoManager.Models;
using VideoManager.Repositories;
using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

/// <summary>
/// Unit tests for ImportService retry behavior when calling FFmpeg services.
/// The RetryPipeline is a static field in ImportService configured with Polly:
///   - Max 2 retries, linear backoff (1s, 2s)
///   - Does NOT retry OperationCanceledException
/// </summary>
public class ImportServiceRetryTests : IDisposable
{
    private readonly string _sourceDir;
    private readonly string _libraryDir;
    private readonly string _thumbnailDir;
    private readonly Mock<IFFmpegService> _mockFfmpeg;
    private readonly Mock<IVideoRepository> _mockRepo;
    private readonly ImportService _service;

    public ImportServiceRetryTests()
    {
        // Create real temp directories for file I/O in Phase 1
        var baseDir = Path.Combine(Path.GetTempPath(), "ImportRetryTests_" + Guid.NewGuid().ToString("N"));
        _sourceDir = Path.Combine(baseDir, "source");
        _libraryDir = Path.Combine(baseDir, "library");
        _thumbnailDir = Path.Combine(baseDir, "thumbnails");

        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_libraryDir);
        Directory.CreateDirectory(_thumbnailDir);

        _mockFfmpeg = new Mock<IFFmpegService>();
        _mockRepo = new Mock<IVideoRepository>();

        // Default: repository AddAsync returns the entry passed in
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<VideoEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((VideoEntry entry, CancellationToken _) => entry);

        // Default: repository AddRangeAsync succeeds (batch write)
        _mockRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<VideoEntry>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var options = Options.Create(new VideoManagerOptions
        {
            VideoLibraryPath = _libraryDir,
            ThumbnailDirectory = _thumbnailDir
        });

        _service = new ImportService(
            _mockFfmpeg.Object,
            _mockRepo.Object,
            options,
            NullLogger<ImportService>.Instance);
    }

    public void Dispose()
    {
        // Clean up temp directories
        var baseDir = Path.GetDirectoryName(_sourceDir)!;
        try
        {
            if (Directory.Exists(baseDir))
                Directory.Delete(baseDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    /// <summary>
    /// Helper: creates a real video file in the source directory for import.
    /// </summary>
    private VideoFileInfo CreateSourceFile(string fileName = "test_video.mp4")
    {
        var filePath = Path.Combine(_sourceDir, fileName);
        File.WriteAllBytes(filePath, new byte[] { 0x00, 0x01, 0x02 });
        return new VideoFileInfo(filePath, fileName, 3);
    }

    #region Successful Retry — ExtractMetadataAsync fails once then succeeds

    [Fact]
    public async Task ImportVideosAsync_ExtractMetadataFailsOnceThenSucceeds_ImportsWithCorrectMetadata()
    {
        // Arrange
        var file = CreateSourceFile();
        var expectedMetadata = new VideoMetadata(TimeSpan.FromMinutes(5), 1920, 1080, "h264", 5_000_000);

        var callCount = 0;
        _mockFfmpeg
            .Setup(f => f.ExtractMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((_, _) =>
            {
                callCount++;
                if (callCount == 1)
                    throw new IOException("Transient FFmpeg failure");
                return Task.FromResult(expectedMetadata);
            });

        _mockFfmpeg
            .Setup(f => f.GenerateThumbnailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/thumbnails/thumb.jpg");

        var progress = new Progress<ImportProgress>();

        // Act
        var result = await _service.ImportVideosAsync(
            new List<VideoFileInfo> { file },
            ImportMode.Copy,
            progress,
            CancellationToken.None);

        // Assert — import should succeed
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.FailCount);

        // ExtractMetadataAsync should have been called exactly 2 times (1 fail + 1 success)
        Assert.Equal(2, callCount);

        // The saved entry should have the correct metadata from the successful retry
        _mockRepo.Verify(r => r.AddRangeAsync(
            It.Is<IEnumerable<VideoEntry>>(entries =>
                entries.Any(e =>
                    e.Duration == expectedMetadata.Duration &&
                    e.Width == expectedMetadata.Width &&
                    e.Height == expectedMetadata.Height &&
                    e.Codec == expectedMetadata.Codec &&
                    e.Bitrate == expectedMetadata.Bitrate)),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Retry Exhausted — ExtractMetadataAsync fails 3 times (exceeds max 2 retries)

    [Fact]
    public async Task ImportVideosAsync_ExtractMetadataFailsAllRetries_UsesDefaultMetadataAndSucceeds()
    {
        // Arrange
        var file = CreateSourceFile("retry_exhaust.mp4");

        var callCount = 0;
        _mockFfmpeg
            .Setup(f => f.ExtractMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((_, _) =>
            {
                callCount++;
                throw new IOException($"Persistent FFmpeg failure #{callCount}");
            });

        _mockFfmpeg
            .Setup(f => f.GenerateThumbnailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/thumbnails/thumb.jpg");

        var progress = new Progress<ImportProgress>();

        // Act
        var result = await _service.ImportVideosAsync(
            new List<VideoFileInfo> { file },
            ImportMode.Copy,
            progress,
            CancellationToken.None);

        // Assert — import should still succeed with default metadata
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.FailCount);

        // ExtractMetadataAsync should have been called exactly 3 times (1 initial + 2 retries)
        Assert.Equal(3, callCount);

        // The saved entry should have default metadata (TimeSpan.Zero, 0, 0, empty codec, 0 bitrate)
        _mockRepo.Verify(r => r.AddRangeAsync(
            It.Is<IEnumerable<VideoEntry>>(entries =>
                entries.Any(e =>
                    e.Duration == TimeSpan.Zero &&
                    e.Width == 0 &&
                    e.Height == 0 &&
                    e.Codec == string.Empty &&
                    e.Bitrate == 0)),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Cancellation Not Retried — OperationCanceledException propagates immediately

    [Fact]
    public async Task ImportVideosAsync_ExtractMetadataThrowsCancellation_DoesNotRetryAndPropagates()
    {
        // Arrange
        var file = CreateSourceFile("cancel_test.mp4");
        var cts = new CancellationTokenSource();

        var callCount = 0;
        _mockFfmpeg
            .Setup(f => f.ExtractMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((_, ct) =>
            {
                callCount++;
                throw new OperationCanceledException(ct);
            });

        _mockFfmpeg
            .Setup(f => f.GenerateThumbnailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/thumbnails/thumb.jpg");

        var progress = new Progress<ImportProgress>();

        // Act & Assert — OperationCanceledException should propagate
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _service.ImportVideosAsync(
                new List<VideoFileInfo> { file },
                ImportMode.Copy,
                progress,
                cts.Token));

        // ExtractMetadataAsync should have been called exactly once (no retries)
        Assert.Equal(1, callCount);

        // No entry should have been saved to the repository
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<VideoEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepo.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<VideoEntry>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion
}
