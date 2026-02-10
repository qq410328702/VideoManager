using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for AsNoTracking query result consistency.
/// **Feature: video-manager-optimization-v2, Property 1: AsNoTracking 查询结果一致性**
/// **Validates: Requirements 1.1, 1.3**
///
/// For any search criteria and video collection in the database,
/// the result set (ID, Title, Tags count) returned by an AsNoTracking search query
/// should be identical to the result returned by a tracked query.
/// </summary>
public class AsNoTrackingPropertyTests
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
    /// Generates a random scenario configuration as an int array:
    /// [videoCount, tagCount, keywordFlag, tagFilterFlag, seed]
    /// </summary>
    private static FsCheck.Arbitrary<int[]> AsNoTrackingScenarioArb()
    {
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, 99999)),
            arr =>
            {
                var videoCount = arr.Length > 0 ? (arr[0] % 8) + 2 : 3;       // 2-9 videos
                var tagCount = arr.Length > 1 ? (arr[1] % 4) + 1 : 2;         // 1-4 tags
                var keywordFlag = arr.Length > 2 ? arr[2] % 2 : 0;            // 0 or 1
                var tagFilterFlag = arr.Length > 3 ? arr[3] % 2 : 0;          // 0 or 1
                var seed = arr.Length > 4 ? Math.Abs(arr[4]) + 1 : 1;         // positive seed
                return new int[] { videoCount, tagCount, keywordFlag, tagFilterFlag, seed };
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 5));
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v2, Property 1: AsNoTracking 查询结果一致性**
    /// **Validates: Requirements 1.1, 1.3**
    ///
    /// For any search criteria and video collection in the database,
    /// the result set (ID, Title, Tags count) returned by an AsNoTracking search query
    /// (via SearchService.SearchAsync) should be identical to the result obtained by
    /// a tracked query (manual LINQ with Include but without AsNoTracking) on the same data.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property AsNoTrackingQueryResultConsistency()
    {
        return FsCheck.Fluent.Prop.ForAll(AsNoTrackingScenarioArb(), config =>
        {
            int videoCount = config[0];
            int tagCount = config[1];
            bool useKeyword = config[2] == 1;
            bool useTagFilter = config[3] == 1;
            int seed = config[4];

            // Use a shared connection string with unique DB name per test run
            // to allow two separate contexts to see the same data.
            var connectionString = $"DataSource=AsNoTrackingTest_{seed}_{videoCount}_{tagCount};Mode=Memory;Cache=Shared";

            var optionsBuilder = new DbContextOptionsBuilder<VideoManagerDbContext>()
                .UseSqlite(connectionString);

            using var setupContext = new VideoManagerDbContext(optionsBuilder.Options);
            setupContext.Database.OpenConnection();
            setupContext.Database.EnsureCreated();

            try
            {
                var rng = new Random(seed);
                var keywords = new[] { "Alpha", "Beta", "Gamma", "Delta" };

                // --- Setup: create tags ---
                var tags = new List<Tag>();
                for (int t = 0; t < tagCount; t++)
                {
                    var tag = new Tag { Name = $"Tag_{seed}_{t}" };
                    setupContext.Tags.Add(tag);
                    tags.Add(tag);
                }
                setupContext.SaveChanges();

                // --- Setup: create videos with varied properties ---
                for (int i = 0; i < videoCount; i++)
                {
                    var kw = keywords[rng.Next(keywords.Length)];
                    var video = new VideoEntry
                    {
                        Title = $"{kw}_Video_{seed}_{i}",
                        Description = rng.Next(2) == 1 ? $"Desc about {kw}" : null,
                        FileName = $"video_{seed}_{i}.mp4",
                        FilePath = $"/videos/video_{seed}_{i}.mp4",
                        FileSize = 1024L * (i + 1),
                        Duration = TimeSpan.FromMinutes(rng.Next(1, 60)),
                        Width = 1920,
                        Height = 1080,
                        ImportedAt = new DateTime(2024, 1, 1).AddDays(rng.Next(0, 365)),
                        CreatedAt = DateTime.UtcNow
                    };

                    // Randomly assign tags
                    foreach (var tag in tags)
                    {
                        if (rng.Next(2) == 1)
                            video.Tags.Add(tag);
                    }

                    setupContext.VideoEntries.Add(video);
                }
                setupContext.SaveChanges();
                setupContext.ChangeTracker.Clear();

                // --- Build search criteria ---
                string? keyword = useKeyword ? keywords[seed % keywords.Length] : null;
                List<int>? tagIds = useTagFilter && tags.Count > 0
                    ? new List<int> { tags[seed % tags.Count].Id }
                    : null;

                var criteria = new SearchCriteria(keyword, tagIds, null, null, null, null);

                // --- Query 1: AsNoTracking via SearchService (the production path) ---
                using var asNoTrackingContext = new VideoManagerDbContext(optionsBuilder.Options);
                asNoTrackingContext.Database.OpenConnection();
                var searchService = new SearchService(asNoTrackingContext, NullLogger<SearchService>.Instance);
                var asNoTrackingResult = searchService.SearchAsync(criteria, 1, 1000, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // --- Query 2: Tracked query (manual, without AsNoTracking) ---
                using var trackedContext = new VideoManagerDbContext(optionsBuilder.Options);
                trackedContext.Database.OpenConnection();

                IQueryable<VideoEntry> trackedQuery = trackedContext.VideoEntries
                    .Include(v => v.Tags)
                    .Include(v => v.Categories);

                // Apply same filters manually
                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    var kw = keyword;
                    trackedQuery = trackedQuery.Where(v =>
                        EF.Functions.Like(v.Title, $"%{kw}%") ||
                        (v.Description != null && EF.Functions.Like(v.Description, $"%{kw}%")));
                }

                if (tagIds is { Count: > 0 })
                {
                    trackedQuery = trackedQuery.Where(v => v.Tags.Any(t => tagIds.Contains(t.Id)));
                }

                var trackedItems = trackedQuery
                    .OrderByDescending(v => v.ImportedAt)
                    .ToList();

                // --- Verify: result sets are identical ---

                // Same count
                bool countMatch = asNoTrackingResult.Items.Count == trackedItems.Count;
                bool totalCountMatch = asNoTrackingResult.TotalCount == trackedItems.Count;

                // Same IDs (order may differ due to same ImportedAt, so compare sorted)
                var asNoTrackingIds = asNoTrackingResult.Items
                    .Select(v => v.Id).OrderBy(id => id).ToList();
                var trackedIds = trackedItems
                    .Select(v => v.Id).OrderBy(id => id).ToList();
                bool idsMatch = asNoTrackingIds.SequenceEqual(trackedIds);

                // Same Titles
                var asNoTrackingTitles = asNoTrackingResult.Items
                    .OrderBy(v => v.Id).Select(v => v.Title).ToList();
                var trackedTitles = trackedItems
                    .OrderBy(v => v.Id).Select(v => v.Title).ToList();
                bool titlesMatch = asNoTrackingTitles.SequenceEqual(trackedTitles);

                // Same Tags count per video
                var asNoTrackingTagCounts = asNoTrackingResult.Items
                    .OrderBy(v => v.Id).Select(v => v.Tags.Count).ToList();
                var trackedTagCounts = trackedItems
                    .OrderBy(v => v.Id).Select(v => v.Tags.Count).ToList();
                bool tagCountsMatch = asNoTrackingTagCounts.SequenceEqual(trackedTagCounts);

                return countMatch && totalCountMatch && idsMatch && titlesMatch && tagCountsMatch;
            }
            finally
            {
                setupContext.Database.EnsureDeleted();
                setupContext.Database.CloseConnection();
            }
        });
    }
}
