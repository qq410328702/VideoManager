using Microsoft.EntityFrameworkCore;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for batch tag assignment functionality.
/// Tests Property 7: 批量标签分配
///
/// **Feature: video-manager-optimization, Property 7: 批量标签分配**
/// **Validates: Requirements 6.3**
///
/// For any set of video IDs and a Tag ID, after executing batch tag assignment,
/// querying each video's Tags collection should contain that Tag.
/// </summary>
public class BatchTagPropertyTests
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
    /// Generates a batch tag test scenario as an int array:
    /// [videoCount, seed]
    /// videoCount: 1-10 videos to create
    /// seed: positive int for deterministic data generation
    /// </summary>
    private static FsCheck.Arbitrary<int[]> BatchTagScenarioArb()
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
    /// **Feature: video-manager-optimization, Property 7: 批量标签分配**
    /// **Validates: Requirements 6.3**
    ///
    /// For any set of video IDs and a Tag ID, after executing batch tag assignment,
    /// querying each video's Tags collection should contain that Tag.
    /// The operation should be idempotent — calling it twice should not duplicate the tag.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property BatchAddTag_AllVideosShouldContainTag()
    {
        return FsCheck.Fluent.Prop.ForAll(BatchTagScenarioArb(), config =>
        {
            int videoCount = config[0];
            int seed = config[1];

            using var context = CreateInMemoryContext();
            var editService = new EditService(context);

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

            // Setup: create a tag
            var tag = new Tag { Name = $"Tag_{seed}" };
            context.Tags.Add(tag);
            context.SaveChanges();

            // Execute: batch add tag
            editService.BatchAddTagAsync(videoIds, tag.Id, CancellationToken.None)
                .GetAwaiter().GetResult();

            // Verify: every video should contain the tag
            bool allHaveTag = true;
            foreach (var videoId in videoIds)
            {
                var reloaded = context.VideoEntries
                    .Include(v => v.Tags)
                    .First(v => v.Id == videoId);
                if (!reloaded.Tags.Any(t => t.Id == tag.Id))
                {
                    allHaveTag = false;
                    break;
                }
            }

            // Verify idempotency: calling again should not duplicate
            editService.BatchAddTagAsync(videoIds, tag.Id, CancellationToken.None)
                .GetAwaiter().GetResult();

            bool noDuplicates = true;
            foreach (var videoId in videoIds)
            {
                var reloaded = context.VideoEntries
                    .Include(v => v.Tags)
                    .First(v => v.Id == videoId);
                if (reloaded.Tags.Count(t => t.Id == tag.Id) != 1)
                {
                    noDuplicates = false;
                    break;
                }
            }

            return allHaveTag && noDuplicates;
        });
    }
}
