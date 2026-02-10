using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Repositories;
using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for thumbnail generation and path recording.
/// </summary>
public class ThumbnailPropertyTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
            {
                try { Directory.Delete(dir, recursive: true); }
                catch { /* best effort cleanup */ }
            }
        }
    }

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ThumbnailPropTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static VideoManagerDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var context = new VideoManagerDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }

    private static readonly string[] SupportedExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv" };
    private static readonly string Chars = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>
    /// Generates a config array: [nameSeed, extIndex, durationSeconds, width, height]
    /// </summary>
    private static FsCheck.Arbitrary<int[]> ThumbnailConfigArb()
    {
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(1, 99999)),
            arr =>
            {
                if (arr.Length < 5)
                {
                    var padded = new int[5];
                    Array.Copy(arr, padded, arr.Length);
                    for (int i = arr.Length; i < 5; i++)
                        padded[i] = i + 1;
                    return padded;
                }
                return arr.Take(5).ToArray();
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 5));
    }

    private static string SeedToFileName(int seed, int extIndex)
    {
        var nameLen = (Math.Abs(seed) % 20) + 1;
        var chars = new char[nameLen];
        var s = Math.Abs(seed);
        for (int i = 0; i < nameLen; i++)
        {
            chars[i] = Chars[s % Chars.Length];
            s = s / Chars.Length + i + 1;
        }
        var ext = SupportedExtensions[Math.Abs(extIndex) % SupportedExtensions.Length];
        return new string(chars) + ext;
    }

    /// <summary>
    /// **Feature: video-manager, Property 5: 缩略图生成与路径记录**
    /// **Validates: Requirements 2.1, 2.4**
    ///
    /// For any successfully imported video, its VideoEntry's ThumbnailPath field
    /// should point to an actually existing image file.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property ThumbnailGeneratedAndPathRecorded()
    {
        return FsCheck.Fluent.Prop.ForAll(ThumbnailConfigArb(), config =>
        {
            int nameSeed = config[0];
            int extIndex = config[1];
            int durationSeconds = (Math.Abs(config[2]) % 36000) + 1;
            int width = (Math.Abs(config[3]) % 7680) + 1;
            int height = (Math.Abs(config[4]) % 4320) + 1;

            var fileName = SeedToFileName(nameSeed, extIndex);
            var metadata = new VideoMetadata(
                TimeSpan.FromSeconds(durationSeconds), width, height, "h264", 5000000);

            // Setup temp directories
            var sourceDir = CreateTempDir();
            var videoLibraryDir = CreateTempDir();
            var thumbnailDir = CreateTempDir();

            // Create a real source file
            var sourceFilePath = Path.Combine(sourceDir, fileName);
            File.WriteAllBytes(sourceFilePath, new byte[] { 0x00, 0x01, 0x02, 0x03 });
            var fileSize = new FileInfo(sourceFilePath).Length;

            // Mock IFFmpegService:
            // - ExtractMetadataAsync returns valid metadata
            // - GenerateThumbnailAsync creates a real .jpg file in the output directory and returns its path
            var mockFfmpeg = new Mock<IFFmpegService>();
            mockFfmpeg.Setup(f => f.ExtractMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(metadata);
            mockFfmpeg.Setup(f => f.GenerateThumbnailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string videoPath, string outDir, CancellationToken _) =>
                {
                    // Create a real thumbnail file on disk
                    var thumbFileName = Path.GetFileNameWithoutExtension(videoPath) + ".jpg";
                    var thumbPath = Path.Combine(outDir, thumbFileName);
                    // Write minimal JPEG header bytes to simulate a real image file
                    File.WriteAllBytes(thumbPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
                    return thumbPath;
                });

            // Use real SQLite In-Memory database
            using var context = CreateInMemoryContext();
            var videoRepo = new VideoRepository(context);

            var importService = new ImportService(
                mockFfmpeg.Object,
                videoRepo,
                Options.Create(new VideoManagerOptions
                {
                    VideoLibraryPath = videoLibraryDir,
                    ThumbnailDirectory = thumbnailDir
                }),
                NullLogger<ImportService>.Instance);

            // Execute import
            var files = new List<VideoFileInfo>
            {
                new(sourceFilePath, fileName, fileSize)
            };
            var result = importService.ImportVideosAsync(
                files, ImportMode.Copy, new Progress<ImportProgress>(), CancellationToken.None)
                .GetAwaiter().GetResult();

            // Verify: import succeeded
            bool importSucceeded = result.SuccessCount == 1 && result.FailCount == 0;

            // Verify: VideoEntry exists in database
            var entries = context.VideoEntries.ToList();
            bool entryExists = entries.Count == 1;

            // Verify: ThumbnailPath is not null/empty
            bool thumbnailPathSet = false;
            bool thumbnailFileExists = false;

            if (entryExists)
            {
                var entry = entries[0];

                // ThumbnailPath should be set (non-null, non-empty)
                thumbnailPathSet = !string.IsNullOrEmpty(entry.ThumbnailPath);

                // The file at ThumbnailPath should actually exist on disk
                if (thumbnailPathSet)
                {
                    thumbnailFileExists = File.Exists(entry.ThumbnailPath);
                }
            }

            return importSucceeded && entryExists && thumbnailPathSet && thumbnailFileExists;
        });
    }
}
