using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for batch category move functionality.
/// Tests Property 8: 批量分类移动
///
/// **Feature: video-manager-optimization, Property 8: 批量分类移动**
/// **Validates: Requirements 6.4**
///
/// For any set of video IDs and a Folder_Category ID, after executing batch category move,
/// querying each video's Categories collection should contain that Folder_Category.
/// </summary>
public class BatchCategoryPropertyTests
{
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
    /// Generates a batch category test scenario as an int array:
    /// [videoCount, seed]
    /// videoCount: 1-10 videos to create
    /// seed: positive int for deterministic data generation
    /// </summary>
    private static FsCheck.Arbitrary<int[]> BatchCategoryScenarioArb()
    {
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(1, 99999)),
            arr =>
            {
                var videoCount = arr.Length > 0 ? (arr[0] % 10) + 1 : 1;  // 1-10 videos
                var seed = arr.Length > 1 ? Math.Abs(arr[1]) + 1 : 1;
                return new int[] { videoCount, seed };
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 2));
    }

    /// <summary>
    /// **Feature: video-manager-optimization, Property 8: 批量分类移动**
    /// **Validates: Requirements 6.4**
    ///
    /// For any set of video IDs and a Folder_Category ID, after executing batch category move,
    /// querying each video's Categories collection should contain that Folder_Category.
    /// The operation should be idempotent — calling it twice should not duplicate the category.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property BatchMoveToCategory_AllVideosShouldContainCategory()
    {
        return FsCheck.Fluent.Prop.ForAll(BatchCategoryScenarioArb(), config =>
        {
            int videoCount = config[0];
            int seed = config[1];

            using var context = CreateInMemoryContext();
            var editService = new EditService(context, NullLogger<EditService>.Instance);

            // Setup: create videos
            var videoIds = new List<int>();
            for (int i = 0; i < videoCount; i++)
            {
                var video = new VideoEntry
                {
                    Title = $"Video_{seed}_{i}",
                    FileName = $"video_{seed}_{i}.mp4",
                    FilePath = $"/videos/video_{seed}_{i}.mp4",
                    FileSize = 1024 * ((seed + i) % 100 + 1),
                    Duration = TimeSpan.FromMinutes((seed + i) % 60 + 1),
                    Width = 1920,
                    Height = 1080,
                    ImportedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                context.VideoEntries.Add(video);
                context.SaveChanges();
                videoIds.Add(video.Id);
            }

            // Setup: create a category
            var category = new FolderCategory { Name = $"Category_{seed}" };
            context.FolderCategories.Add(category);
            context.SaveChanges();

            // Execute: batch move to category
            editService.BatchMoveToCategoryAsync(videoIds, category.Id, CancellationToken.None)
                .GetAwaiter().GetResult();

            // Verify: every video should contain the category
            bool allHaveCategory = true;
            foreach (var videoId in videoIds)
            {
                var reloaded = context.VideoEntries
                    .Include(v => v.Categories)
                    .First(v => v.Id == videoId);
                if (!reloaded.Categories.Any(c => c.Id == category.Id))
                {
                    allHaveCategory = false;
                    break;
                }
            }

            // Verify idempotency: calling again should not duplicate
            editService.BatchMoveToCategoryAsync(videoIds, category.Id, CancellationToken.None)
                .GetAwaiter().GetResult();

            bool noDuplicates = true;
            foreach (var videoId in videoIds)
            {
                var reloaded = context.VideoEntries
                    .Include(v => v.Categories)
                    .First(v => v.Id == videoId);
                if (reloaded.Categories.Count(c => c.Id == category.Id) != 1)
                {
                    noDuplicates = false;
                    break;
                }
            }

            return allHaveCategory && noDuplicates;
        });
    }
}
