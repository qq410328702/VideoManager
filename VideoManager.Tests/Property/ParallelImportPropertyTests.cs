using System.IO;
using System.Threading;
using Microsoft.Extensions.Options;
using Moq;
using VideoManager.Models;
using VideoManager.Repositories;
using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for parallel import consistency.
/// Tests Property 10: 并行导入一致性
///
/// **Feature: video-manager-optimization, Property 10: 并行导入一致性**
/// **Validates: Requirements 9.1, 9.2**
///
/// For any batch of video files, the parallel import result
/// (SuccessCount + FailCount == total count, and each successfully imported video
/// has a corresponding database record) should be consistent.
/// </summary>
public class ParallelImportPropertyTests : IDisposable
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

    private string CreateTempDir(string suffix = "")
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ParallelImportPropTest_{suffix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    /// <summary>
    /// Generates a parallel import test scenario as an int array:
    /// [fileCount]
    /// fileCount: 1-10 video files to import (all succeed)
    /// </summary>
    private static FsCheck.Arbitrary<int[]> ParallelImportScenarioArb()
    {
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(1, 10)),
            arr =>
            {
                var fileCount = arr.Length > 0 ? (arr[0] % 10) + 1 : 1;  // 1-10 files
                return new int[] { fileCount };
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 1));
    }

    /// <summary>
    /// Creates source video files in a temp directory and returns the list of VideoFileInfo.
    /// </summary>
    private static List<VideoFileInfo> CreateSourceFiles(string sourceDir, int fileCount)
    {
        var files = new List<VideoFileInfo>();
        var guid = Guid.NewGuid().ToString("N")[..8];
        for (int i = 0; i < fileCount; i++)
        {
            var fileName = $"video_{guid}_{i}.mp4";
            var filePath = Path.Combine(sourceDir, fileName);
            File.WriteAllBytes(filePath, new byte[] { 0x00, 0x01, 0x02, (byte)(i % 256) });
            files.Add(new VideoFileInfo(filePath, fileName, 4));
        }
        return files;
    }

    /// <summary>
    /// **Feature: video-manager-optimization, Property 10: 并行导入一致性**
    /// **Validates: Requirements 9.1, 9.2**
    ///
    /// For any batch of video files (1-10), after parallel import:
    /// 1. SuccessCount + FailCount == total file count
    /// 2. Each successfully imported video has a corresponding repository AddAsync call
    /// 3. Progress reports are consistent (final completed == total)
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property ParallelImport_SuccessPlusFailEqualsTotalAndProgressConsistent()
    {
        return FsCheck.Fluent.Prop.ForAll(ParallelImportScenarioArb(), config =>
        {
            int fileCount = config[0];

            var sourceDir = CreateTempDir("src");
            var libraryDir = CreateTempDir("lib");
            var thumbnailDir = CreateTempDir("thumb");

            var files = CreateSourceFiles(sourceDir, fileCount);

            // Track AddAsync calls
            var addedCount = 0;
            var addedLock = new object();

            // Setup mock FFmpegService
            var ffmpegMock = new Mock<IFFmpegService>();
            ffmpegMock.Setup(f => f.ExtractMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(new VideoMetadata(
                    TimeSpan.FromMinutes(5), 1920, 1080, "h264", 5000000)));

            ffmpegMock.Setup(f => f.GenerateThumbnailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns<string, string, CancellationToken>((path, outDir, ct) =>
                    Task.FromResult(Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".jpg")));

            // Setup mock VideoRepository - all succeed
            var repoMock = new Mock<IVideoRepository>();
            repoMock.Setup(r => r.AddAsync(It.IsAny<VideoEntry>(), It.IsAny<CancellationToken>()))
                .Returns<VideoEntry, CancellationToken>((entry, ct) =>
                {
                    lock (addedLock)
                    {
                        addedCount++;
                    }
                    return Task.FromResult(entry);
                });

            var options = Options.Create(new VideoManagerOptions
            {
                VideoLibraryPath = libraryDir,
                ThumbnailDirectory = thumbnailDir
            });

            var importService = new ImportService(ffmpegMock.Object, repoMock.Object, options);

            // Track progress using a thread-safe list
            var maxCompleted = 0;
            var progressTotal = 0;
            var progressLock = new object();
            var progress = new Progress<ImportProgress>(p =>
            {
                lock (progressLock)
                {
                    if (p.Completed > maxCompleted)
                        maxCompleted = p.Completed;
                    progressTotal = p.Total;
                }
            });

            // Execute import
            var result = importService.ImportVideosAsync(files, ImportMode.Copy, progress, CancellationToken.None)
                .GetAwaiter().GetResult();

            // Allow Progress<T> callbacks to complete
            Thread.Sleep(300);

            // Property 1: SuccessCount + FailCount == total file count
            bool countConsistent = result.SuccessCount + result.FailCount == fileCount;

            // Property 2: All should succeed (no simulated failures)
            bool allSucceeded = result.SuccessCount == fileCount && result.FailCount == 0;

            // Property 3: Number of AddAsync calls matches SuccessCount
            int finalAddedCount;
            lock (addedLock)
            {
                finalAddedCount = addedCount;
            }
            bool addCountMatchesSuccess = finalAddedCount == result.SuccessCount;

            // Property 4: Progress total is consistent
            int finalMaxCompleted, finalProgressTotal;
            lock (progressLock)
            {
                finalMaxCompleted = maxCompleted;
                finalProgressTotal = progressTotal;
            }
            bool progressConsistent = finalProgressTotal == fileCount
                && finalMaxCompleted == fileCount;

            return countConsistent && allSucceeded && addCountMatchesSuccess && progressConsistent;
        });
    }
}
