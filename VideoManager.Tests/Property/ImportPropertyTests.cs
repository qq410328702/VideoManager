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
/// Property-based tests for the video import round-trip.
/// </summary>
public class ImportPropertyTests : IDisposable
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
        var dir = Path.Combine(Path.GetTempPath(), "ImportPropTest_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>
    /// Supported video extensions for generating random file names.
    /// </summary>
    private static readonly string[] SupportedExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv" };

    /// <summary>
    /// Generates a combined config array encoding both a file name seed and metadata values.
    /// config[0] = base name seed (used to pick chars), config[1] = extension index,
    /// config[2] = duration seconds, config[3] = width, config[4] = height,
    /// config[5] = codec index, config[6] = bitrate
    /// </summary>
    private static FsCheck.Arbitrary<int[]> ImportConfigArb()
    {
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(1, 99999)),
            arr =>
            {
                // Ensure we have at least 7 elements
                if (arr.Length < 7)
                {
                    var padded = new int[7];
                    Array.Copy(arr, padded, arr.Length);
                    for (int i = arr.Length; i < 7; i++)
                        padded[i] = i + 1;
                    return padded;
                }
                return arr.Take(7).ToArray();
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 7));
    }

    private static readonly string Chars = "abcdefghijklmnopqrstuvwxyz0123456789";
    private static readonly string[] Codecs = { "h264", "hevc", "vp9", "av1", "mpeg4" };

    private static string SeedToFileName(int seed, int extIndex)
    {
        // Generate a deterministic file name from the seed
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
    /// Generates a pair (validCount, invalidCount) each in [1, 5].
    /// </summary>
    private static FsCheck.Arbitrary<int[]> FailureIsolationCountArb()
    {
        var gen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(1, 5)),
            arr =>
            {
                if (arr.Length < 2)
                {
                    var padded = new int[2];
                    Array.Copy(arr, padded, arr.Length);
                    for (int i = arr.Length; i < 2; i++)
                        padded[i] = 1;
                    return padded;
                }
                return arr.Take(2).ToArray();
            });
        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(gen, c => c.Length == 2));
    }

    /// <summary>
    /// **Feature: video-manager, Property 3: 导入失败隔离**
    /// **Validates: Requirements 1.6**
    ///
    /// For any batch containing valid and invalid file paths, after import,
    /// all valid files should be successfully imported, all invalid files should
    /// be recorded in the failure list, and failed files don't affect valid file imports.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property ImportFailureIsolation()
    {
        return FsCheck.Fluent.Prop.ForAll(FailureIsolationCountArb(), counts =>
        {
            int validCount = counts[0];
            int invalidCount = counts[1];

            // Setup temp directories
            var sourceDir = CreateTempDir();
            var videoLibraryDir = CreateTempDir();
            var thumbnailDir = CreateTempDir();

            // Create real temp files for valid entries
            var validFiles = new List<VideoFileInfo>();
            for (int i = 0; i < validCount; i++)
            {
                var fileName = $"valid_{i}.mp4";
                var filePath = Path.Combine(sourceDir, fileName);
                File.WriteAllBytes(filePath, new byte[] { 0x00, 0x01, 0x02 });
                var fileSize = new FileInfo(filePath).Length;
                validFiles.Add(new VideoFileInfo(filePath, fileName, fileSize));
            }

            // Create non-existent paths for invalid entries
            var invalidFiles = new List<VideoFileInfo>();
            var invalidPaths = new List<string>();
            for (int i = 0; i < invalidCount; i++)
            {
                var fileName = $"invalid_{i}.mp4";
                var filePath = Path.Combine(sourceDir, "nonexistent_subdir", fileName);
                invalidPaths.Add(filePath);
                invalidFiles.Add(new VideoFileInfo(filePath, fileName, 999));
            }

            // Combine: interleave valid and invalid to ensure ordering doesn't matter
            var allFiles = new List<VideoFileInfo>();
            int vi = 0, ii = 0;
            while (vi < validFiles.Count || ii < invalidFiles.Count)
            {
                if (ii < invalidFiles.Count)
                    allFiles.Add(invalidFiles[ii++]);
                if (vi < validFiles.Count)
                    allFiles.Add(validFiles[vi++]);
            }

            // Mock IFFmpegService
            var mockFfmpeg = new Mock<IFFmpegService>();
            mockFfmpeg.Setup(f => f.ExtractMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new VideoMetadata(TimeSpan.FromSeconds(60), 1920, 1080, "h264", 5000000));
            mockFfmpeg.Setup(f => f.GenerateThumbnailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string videoPath, string outDir, CancellationToken _) =>
                {
                    var thumbPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(videoPath) + ".jpg");
                    File.WriteAllBytes(thumbPath, new byte[] { 0xFF, 0xD8 });
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
            var result = importService.ImportVideosAsync(
                allFiles, ImportMode.Copy, new Progress<ImportProgress>(), CancellationToken.None)
                .GetAwaiter().GetResult();

            // Verify: SuccessCount matches valid file count
            bool successCountMatches = result.SuccessCount == validCount;

            // Verify: FailCount matches invalid file count
            bool failCountMatches = result.FailCount == invalidCount;

            // Verify: Errors list contains all invalid file paths
            var errorPaths = result.Errors.Select(e => e.FilePath).ToHashSet();
            bool allInvalidRecorded = invalidPaths.All(p => errorPaths.Contains(p));

            // Verify: all valid files exist in Video_Library
            var libraryFiles = Directory.GetFiles(videoLibraryDir);
            bool allValidInLibrary = libraryFiles.Length == validCount;

            // Verify: database has correct number of entries
            var dbEntries = context.VideoEntries.ToList();
            bool dbCountMatches = dbEntries.Count == validCount;

            return successCountMatches && failCountMatches && allInvalidRecorded && allValidInLibrary && dbCountMatches;
        });
    }

    /// <summary>
    /// **Feature: video-manager, Property 2: 导入 round-trip**
    /// **Validates: Requirements 1.3, 1.8**
    ///
    /// For any valid video file, after importing to Video_Library, the file should
    /// exist in the Video_Library directory, and a corresponding VideoEntry record
    /// should exist in the database with valid metadata fields (Duration, Width,
    /// Height are non-zero/non-default).
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property ImportRoundTrip()
    {
        return FsCheck.Fluent.Prop.ForAll(ImportConfigArb(), config =>
        {
            int nameSeed = config[0];
            int extIndex = config[1];
            int durationSeconds = (Math.Abs(config[2]) % 36000) + 1; // 1..36000
            int width = (Math.Abs(config[3]) % 7680) + 1;            // 1..7680
            int height = (Math.Abs(config[4]) % 4320) + 1;           // 1..4320
            int codecIndex = config[5];
            long bitrate = ((long)Math.Abs(config[6]) % 50000000) + 100000; // 100000..50099999

            var fileName = SeedToFileName(nameSeed, extIndex);
            var duration = TimeSpan.FromSeconds(durationSeconds);
            var codec = Codecs[Math.Abs(codecIndex) % Codecs.Length];
            var metadata = new VideoMetadata(duration, width, height, codec, bitrate);

            // Setup temp directories
            var sourceDir = CreateTempDir();
            var videoLibraryDir = CreateTempDir();
            var thumbnailDir = CreateTempDir();

            // Create a real source file
            var sourceFilePath = Path.Combine(sourceDir, fileName);
            File.WriteAllBytes(sourceFilePath, new byte[] { 0x00, 0x01, 0x02, 0x03 });
            var fileSize = new FileInfo(sourceFilePath).Length;

            // Mock IFFmpegService to return the generated valid metadata
            var mockFfmpeg = new Mock<IFFmpegService>();
            mockFfmpeg.Setup(f => f.ExtractMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(metadata);
            mockFfmpeg.Setup(f => f.GenerateThumbnailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string videoPath, string outDir, CancellationToken _) =>
                {
                    var thumbPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(videoPath) + ".jpg");
                    File.WriteAllBytes(thumbPath, new byte[] { 0xFF, 0xD8 });
                    return thumbPath;
                });

            // Use real SQLite In-Memory database and VideoRepository
            using var context = CreateInMemoryContext();
            var videoRepo = new VideoRepository(context);

            // Create ImportService with real repo and mocked FFmpeg
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

            // Verify: file exists in Video_Library directory
            var libraryFiles = Directory.GetFiles(videoLibraryDir);
            bool fileExistsInLibrary = libraryFiles.Length == 1 && File.Exists(libraryFiles[0]);

            // Verify: a corresponding VideoEntry record exists in the database
            var entries = context.VideoEntries.ToList();
            bool entryExists = entries.Count == 1;

            // Verify: metadata fields are non-zero/non-default and match input
            bool metadataValid = false;
            if (entryExists)
            {
                var entry = entries[0];
                metadataValid =
                    entry.Duration != TimeSpan.Zero &&
                    entry.Width != 0 &&
                    entry.Height != 0 &&
                    entry.Duration == metadata.Duration &&
                    entry.Width == metadata.Width &&
                    entry.Height == metadata.Height;
            }

            return importSucceeded && fileExistsInLibrary && entryExists && metadataValid;
        });
    }

    /// <summary>
    /// **Feature: video-manager, Property 4: 重名文件自动重命名**
    /// **Validates: Requirements 1.7**
    ///
    /// For any two files with the same name, after importing both sequentially,
    /// Video_Library should contain two files with different names, and the second
    /// VideoEntry's OriginalFileName should record the original file name.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property DuplicateFileNameAutoRename()
    {
        return FsCheck.Fluent.Prop.ForAll(ImportConfigArb(), config =>
        {
            int nameSeed = config[0];
            int extIndex = config[1];
            int durationSeconds = (Math.Abs(config[2]) % 36000) + 1;
            int width = (Math.Abs(config[3]) % 7680) + 1;
            int height = (Math.Abs(config[4]) % 4320) + 1;
            int codecIndex = config[5];
            long bitrate = ((long)Math.Abs(config[6]) % 50000000) + 100000;

            var fileName = SeedToFileName(nameSeed, extIndex);
            var duration = TimeSpan.FromSeconds(durationSeconds);
            var codec = Codecs[Math.Abs(codecIndex) % Codecs.Length];
            var metadata = new VideoMetadata(duration, width, height, codec, bitrate);

            // Setup temp directories
            var sourceDir1 = CreateTempDir();
            var sourceDir2 = CreateTempDir();
            var videoLibraryDir = CreateTempDir();
            var thumbnailDir = CreateTempDir();

            // Create two source files with the SAME file name in different directories
            var sourceFilePath1 = Path.Combine(sourceDir1, fileName);
            File.WriteAllBytes(sourceFilePath1, new byte[] { 0x00, 0x01, 0x02 });
            var fileSize1 = new FileInfo(sourceFilePath1).Length;

            var sourceFilePath2 = Path.Combine(sourceDir2, fileName);
            File.WriteAllBytes(sourceFilePath2, new byte[] { 0x03, 0x04, 0x05 });
            var fileSize2 = new FileInfo(sourceFilePath2).Length;

            // Mock IFFmpegService
            var mockFfmpeg = new Mock<IFFmpegService>();
            mockFfmpeg.Setup(f => f.ExtractMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(metadata);
            mockFfmpeg.Setup(f => f.GenerateThumbnailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string videoPath, string outDir, CancellationToken _) =>
                {
                    var thumbPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(videoPath) + ".jpg");
                    File.WriteAllBytes(thumbPath, new byte[] { 0xFF, 0xD8 });
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

            // Import first file
            var result1 = importService.ImportVideosAsync(
                new List<VideoFileInfo> { new(sourceFilePath1, fileName, fileSize1) },
                ImportMode.Copy,
                new Progress<ImportProgress>(),
                CancellationToken.None)
                .GetAwaiter().GetResult();

            // Import second file with the same name
            var result2 = importService.ImportVideosAsync(
                new List<VideoFileInfo> { new(sourceFilePath2, fileName, fileSize2) },
                ImportMode.Copy,
                new Progress<ImportProgress>(),
                CancellationToken.None)
                .GetAwaiter().GetResult();

            // Verify: both imports succeeded
            bool bothSucceeded = result1.SuccessCount == 1 && result1.FailCount == 0
                              && result2.SuccessCount == 1 && result2.FailCount == 0;

            // Verify: Video_Library contains exactly 2 files
            var libraryFiles = Directory.GetFiles(videoLibraryDir);
            bool twoFilesInLibrary = libraryFiles.Length == 2;

            // Verify: the two files have different names
            var libraryFileNames = libraryFiles.Select(Path.GetFileName).ToList();
            bool differentNames = twoFilesInLibrary
                && libraryFileNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2;

            // Verify: database has 2 entries
            var entries = context.VideoEntries.OrderBy(e => e.Id).ToList();
            bool twoEntries = entries.Count == 2;

            // Verify: the second entry's OriginalFileName records the original file name
            bool secondEntryRecordsOriginal = false;
            if (twoEntries)
            {
                var secondEntry = entries[1];
                // The second entry should have OriginalFileName set to the original file name
                // because it was renamed to avoid conflict
                secondEntryRecordsOriginal = secondEntry.OriginalFileName == fileName;

                // Also verify the second entry's FileName differs from the original
                secondEntryRecordsOriginal = secondEntryRecordsOriginal
                    && secondEntry.FileName != fileName;
            }

            return bothSucceeded && twoFilesInLibrary && differentNames && twoEntries && secondEntryRecordsOriginal;
        });
    }

}
