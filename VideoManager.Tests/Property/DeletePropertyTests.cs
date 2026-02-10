using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for video deletion functionality.
/// Tests Property 11 (remove from library only) and Property 12 (delete with source files).
/// </summary>
public class DeletePropertyTests : IDisposable
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
        var dir = Path.Combine(Path.GetTempPath(), "DeletePropTest_" + Guid.NewGuid().ToString("N"));
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
    /// Generates a delete test scenario as an int array:
    /// [seed, tagCount, categoryCount]
    /// seed: positive int for deterministic data generation
    /// tagCount: 0-3 tags to associate with the video
    /// categoryCount: 0-3 categories to associate with the video
    /// </summary>
    private static FsCheck.Arbitrary<int[]> DeleteScenarioArb()
    {
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(1, 99999)),
            arr =>
            {
                var seed = arr.Length > 0 ? Math.Abs(arr[0]) + 1 : 1;
                var tagCount = arr.Length > 1 ? arr[1] % 4 : 0;           // 0-3 tags
                var categoryCount = arr.Length > 2 ? arr[2] % 4 : 0;      // 0-3 categories
                return new int[] { seed, tagCount, categoryCount };
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 3));
    }

    /// <summary>
    /// Helper to create a VideoEntry with associated tags and categories in the database,
    /// and optionally create real files on disk.
    /// Returns the video ID, video file path, and thumbnail file path.
    /// </summary>
    private static (int videoId, string videoFilePath, string thumbnailFilePath) SetupVideoEntry(
        VideoManagerDbContext context, int seed, int tagCount, int categoryCount,
        string tempDir, bool createFiles)
    {
        var videoFilePath = Path.Combine(tempDir, $"video_{seed}.mp4");
        var thumbnailFilePath = Path.Combine(tempDir, $"thumb_{seed}.jpg");

        if (createFiles)
        {
            File.WriteAllBytes(videoFilePath, new byte[] { 0x00, 0x01, 0x02, 0x03 });
            File.WriteAllBytes(thumbnailFilePath, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
        }

        var video = new VideoEntry
        {
            Title = $"Video_{seed}",
            FileName = $"video_{seed}.mp4",
            FilePath = videoFilePath,
            ThumbnailPath = thumbnailFilePath,
            FileSize = 1024 * (seed % 100 + 1),
            Duration = TimeSpan.FromMinutes(seed % 60 + 1),
            Width = 1920,
            Height = 1080,
            ImportedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        context.VideoEntries.Add(video);
        context.SaveChanges();

        // Add tags
        for (int i = 0; i < tagCount; i++)
        {
            var tag = new Tag { Name = $"Tag_{seed}_{i}" };
            context.Tags.Add(tag);
            context.SaveChanges();
            video.Tags.Add(tag);
        }

        // Add categories
        for (int i = 0; i < categoryCount; i++)
        {
            var category = new FolderCategory { Name = $"Category_{seed}_{i}" };
            context.FolderCategories.Add(category);
            context.SaveChanges();
            video.Categories.Add(category);
        }

        if (tagCount > 0 || categoryCount > 0)
        {
            context.SaveChanges();
        }

        return (video.Id, videoFilePath, thumbnailFilePath);
    }

    /// <summary>
    /// **Feature: video-manager-optimization, Property 11: 仅从库中删除**
    /// **Validates: Requirements 12.1**
    ///
    /// For any VideoEntry in DB, after "remove from library only" delete,
    /// DB record should not exist but video file should still exist on disk.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property RemoveFromLibraryOnly_DbRecordDeleted_FilesPreserved()
    {
        return FsCheck.Fluent.Prop.ForAll(DeleteScenarioArb(), config =>
        {
            int seed = config[0];
            int tagCount = config[1];
            int categoryCount = config[2];

            var tempDir = CreateTempDir();
            using var context = CreateInMemoryContext();
            var deleteService = new DeleteService(context, NullLogger<DeleteService>.Instance);

            // Setup: create video entry with real files on disk
            var (videoId, videoFilePath, thumbnailFilePath) = SetupVideoEntry(
                context, seed, tagCount, categoryCount, tempDir, createFiles: true);

            // Verify preconditions: video exists in DB and files exist on disk
            bool videoExistsBefore = context.VideoEntries.Any(v => v.Id == videoId);
            bool videoFileExistsBefore = File.Exists(videoFilePath);
            bool thumbnailFileExistsBefore = File.Exists(thumbnailFilePath);

            if (!videoExistsBefore || !videoFileExistsBefore || !thumbnailFileExistsBefore)
                return false;

            // Execute: delete with deleteFile=false (remove from library only)
            var result = deleteService.DeleteVideoAsync(videoId, deleteFile: false, CancellationToken.None)
                .GetAwaiter().GetResult();

            // Verify: deletion succeeded
            bool deleteSucceeded = result.Success;

            // Verify: DB record no longer exists
            bool videoNotInDb = !context.VideoEntries.Any(v => v.Id == videoId);

            // Verify: tag and category associations are removed (but tags/categories themselves remain)
            bool tagsStillExist = context.Tags.Count(t => t.Name.StartsWith($"Tag_{seed}_")) == tagCount;
            bool categoriesStillExist = context.FolderCategories.Count(c => c.Name.StartsWith($"Category_{seed}_")) == categoryCount;

            // Verify: video file and thumbnail file still exist on disk
            bool videoFileStillExists = File.Exists(videoFilePath);
            bool thumbnailFileStillExists = File.Exists(thumbnailFilePath);

            return deleteSucceeded
                && videoNotInDb
                && tagsStillExist
                && categoriesStillExist
                && videoFileStillExists
                && thumbnailFileStillExists;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization, Property 12: 删除并移除源文件**
    /// **Validates: Requirements 12.2**
    ///
    /// For any VideoEntry in DB, after "delete with source files" delete,
    /// DB record should not exist AND video/thumbnail files should not exist on disk.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property DeleteWithSourceFiles_DbRecordAndFilesRemoved()
    {
        return FsCheck.Fluent.Prop.ForAll(DeleteScenarioArb(), config =>
        {
            int seed = config[0];
            int tagCount = config[1];
            int categoryCount = config[2];

            var tempDir = CreateTempDir();
            using var context = CreateInMemoryContext();
            var deleteService = new DeleteService(context, NullLogger<DeleteService>.Instance);

            // Setup: create video entry with real files on disk
            var (videoId, videoFilePath, thumbnailFilePath) = SetupVideoEntry(
                context, seed, tagCount, categoryCount, tempDir, createFiles: true);

            // Verify preconditions: video exists in DB and files exist on disk
            bool videoExistsBefore = context.VideoEntries.Any(v => v.Id == videoId);
            bool videoFileExistsBefore = File.Exists(videoFilePath);
            bool thumbnailFileExistsBefore = File.Exists(thumbnailFilePath);

            if (!videoExistsBefore || !videoFileExistsBefore || !thumbnailFileExistsBefore)
                return false;

            // Execute: delete with deleteFile=true (delete with source files)
            var result = deleteService.DeleteVideoAsync(videoId, deleteFile: true, CancellationToken.None)
                .GetAwaiter().GetResult();

            // Verify: deletion succeeded
            bool deleteSucceeded = result.Success;

            // Verify: DB record no longer exists
            bool videoNotInDb = !context.VideoEntries.Any(v => v.Id == videoId);

            // Verify: tag and category associations are removed (but tags/categories themselves remain)
            bool tagsStillExist = context.Tags.Count(t => t.Name.StartsWith($"Tag_{seed}_")) == tagCount;
            bool categoriesStillExist = context.FolderCategories.Count(c => c.Name.StartsWith($"Category_{seed}_")) == categoryCount;

            // Verify: video file and thumbnail file no longer exist on disk
            bool videoFileDeleted = !File.Exists(videoFilePath);
            bool thumbnailFileDeleted = !File.Exists(thumbnailFilePath);

            return deleteSucceeded
                && videoNotInDb
                && tagsStillExist
                && categoriesStillExist
                && videoFileDeleted
                && thumbnailFileDeleted;
        });
    }
}
