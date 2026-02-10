using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for search result correctness.
/// </summary>
public class SearchPropertyTests
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
    /// Represents a generated search scenario: a set of videos and a search criteria to apply.
    /// Encoded as an int array for FsCheck 3.x compatibility:
    /// [videoCount, keywordFlag, tagFlag, dateFlag, durationFlag, seed]
    /// </summary>
    private static FsCheck.Arbitrary<int[]> SearchScenarioArb()
    {
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, 99999)),
            arr =>
            {
                var videoCount = arr.Length > 0 ? (arr[0] % 10) + 2 : 3;       // 2-11 videos
                var keywordFlag = arr.Length > 1 ? arr[1] % 2 : 0;             // 0 or 1
                var tagFlag = arr.Length > 2 ? arr[2] % 2 : 0;                 // 0 or 1
                var dateFlag = arr.Length > 3 ? arr[3] % 2 : 0;                // 0 or 1
                var durationFlag = arr.Length > 4 ? arr[4] % 2 : 0;            // 0 or 1
                var seed = arr.Length > 5 ? Math.Abs(arr[5]) + 1 : 1;          // positive seed
                return new int[] { videoCount, keywordFlag, tagFlag, dateFlag, durationFlag, seed };
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 6));
    }

    /// <summary>
    /// **Feature: video-manager, Property 11: 搜索结果正确性**
    /// **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5**
    ///
    /// For any combination of search criteria (keyword, tag, date range, duration range),
    /// every VideoEntry in the search results should simultaneously satisfy all specified
    /// filter conditions: title or description contains the keyword, has the specified tag,
    /// ImportedAt is within the date range, and Duration is within the duration range.
    /// Multiple criteria use AND logic (intersection).
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property SearchResultCorrectness()
    {
        return FsCheck.Fluent.Prop.ForAll(SearchScenarioArb(), config =>
        {
            int videoCount = config[0];
            bool useKeyword = config[1] == 1;
            bool useTag = config[2] == 1;
            bool useDate = config[3] == 1;
            bool useDuration = config[4] == 1;
            int seed = config[5];

            using var context = CreateInMemoryContext();
            var searchService = new SearchService(context, NullLogger<SearchService>.Instance);
            var ct = CancellationToken.None;

            // --- Setup: create tags ---
            var tag1 = new Tag { Name = $"TagA_{seed}" };
            var tag2 = new Tag { Name = $"TagB_{seed}" };
            context.Tags.Add(tag1);
            context.Tags.Add(tag2);
            context.SaveChanges();

            // --- Setup: create videos with varied properties ---
            var keywords = new[] { "Alpha", "Beta", "Gamma", "Delta", "Epsilon" };
            var rng = new Random(seed);

            for (int i = 0; i < videoCount; i++)
            {
                var kw = keywords[rng.Next(keywords.Length)];
                var hasTag1 = rng.Next(2) == 1;
                var hasTag2 = rng.Next(2) == 1;
                var durationMinutes = rng.Next(1, 121); // 1-120 minutes
                var importDay = rng.Next(1, 365);       // day of year 2024

                var video = new VideoEntry
                {
                    Title = $"{kw}_Video_{seed}_{i}",
                    Description = rng.Next(2) == 1 ? $"Description about {kw} content" : null,
                    FileName = $"video_{seed}_{i}.mp4",
                    FilePath = $"/videos/video_{seed}_{i}.mp4",
                    FileSize = 1024L * (i + 1),
                    Duration = TimeSpan.FromMinutes(durationMinutes),
                    Width = 1920,
                    Height = 1080,
                    ImportedAt = new DateTime(2024, 1, 1).AddDays(importDay - 1),
                    CreatedAt = DateTime.UtcNow
                };

                if (hasTag1) video.Tags.Add(tag1);
                if (hasTag2) video.Tags.Add(tag2);

                context.VideoEntries.Add(video);
            }
            context.SaveChanges();

            // --- Build search criteria based on flags ---
            // Pick a keyword from the set to search for
            string? keyword = useKeyword ? keywords[seed % keywords.Length] : null;

            // Pick tag1 for tag filtering
            List<int>? tagIds = useTag ? new List<int> { tag1.Id } : null;

            // Date range: pick a 6-month window in 2024
            DateTime? dateFrom = useDate ? new DateTime(2024, 4, 1) : null;
            DateTime? dateTo = useDate ? new DateTime(2024, 9, 30) : null;

            // Duration range: 10-60 minutes
            TimeSpan? durationMin = useDuration ? TimeSpan.FromMinutes(10) : null;
            TimeSpan? durationMax = useDuration ? TimeSpan.FromMinutes(60) : null;

            var criteria = new SearchCriteria(keyword, tagIds, dateFrom, dateTo, durationMin, durationMax);

            // --- Execute search (get all results via large page size) ---
            var result = searchService.SearchAsync(criteria, 1, 1000, ct)
                .GetAwaiter().GetResult();

            // --- Verify: every returned item satisfies ALL specified criteria ---
            foreach (var item in result.Items)
            {
                // 4.1: Keyword match on title or description
                if (useKeyword && keyword != null)
                {
                    var kw = keyword.ToLower();
                    bool titleMatch = item.Title.ToLower().Contains(kw);
                    bool descMatch = item.Description != null &&
                                     item.Description.ToLower().Contains(kw);
                    if (!titleMatch && !descMatch)
                        return false;
                }

                // 4.2: Tag filter - video must have at least one of the specified tags
                if (useTag && tagIds != null && tagIds.Count > 0)
                {
                    bool hasMatchingTag = item.Tags.Any(t => tagIds.Contains(t.Id));
                    if (!hasMatchingTag)
                        return false;
                }

                // 4.3: Date range - ImportedAt within range
                if (useDate)
                {
                    if (dateFrom.HasValue && item.ImportedAt < dateFrom.Value)
                        return false;
                    if (dateTo.HasValue && item.ImportedAt > dateTo.Value)
                        return false;
                }

                // 4.4: Duration range - Duration within range
                if (useDuration)
                {
                    if (durationMin.HasValue && item.Duration < durationMin.Value)
                        return false;
                    if (durationMax.HasValue && item.Duration > durationMax.Value)
                        return false;
                }
            }

            // --- Verify: no qualifying video was missed (completeness check) ---
            // Re-query all videos and manually filter to verify TotalCount
            context.ChangeTracker.Clear();
            var allVideos = context.VideoEntries
                .Include(v => v.Tags)
                .ToList();

            var expectedVideos = allVideos.Where(v =>
            {
                // Keyword filter
                if (useKeyword && keyword != null)
                {
                    var kw = keyword.ToLower();
                    bool titleMatch = v.Title.ToLower().Contains(kw);
                    bool descMatch = v.Description != null &&
                                     v.Description.ToLower().Contains(kw);
                    if (!titleMatch && !descMatch)
                        return false;
                }

                // Tag filter
                if (useTag && tagIds != null && tagIds.Count > 0)
                {
                    if (!v.Tags.Any(t => tagIds.Contains(t.Id)))
                        return false;
                }

                // Date range
                if (useDate)
                {
                    if (dateFrom.HasValue && v.ImportedAt < dateFrom.Value)
                        return false;
                    if (dateTo.HasValue && v.ImportedAt > dateTo.Value)
                        return false;
                }

                // Duration range
                if (useDuration)
                {
                    if (durationMin.HasValue && v.Duration < durationMin.Value)
                        return false;
                    if (durationMax.HasValue && v.Duration > durationMax.Value)
                        return false;
                }

                return true;
            }).ToList();

            // TotalCount should match the expected count
            bool totalCountCorrect = result.TotalCount == expectedVideos.Count;

            // Items count should match (since we used a large page size)
            bool itemCountCorrect = result.Items.Count == expectedVideos.Count;

            // All expected video IDs should be present in results
            var expectedIds = expectedVideos.Select(v => v.Id).OrderBy(id => id).ToList();
            var actualIds = result.Items.Select(v => v.Id).OrderBy(id => id).ToList();
            bool idsMatch = expectedIds.SequenceEqual(actualIds);

            return totalCountCorrect && itemCountCorrect && idsMatch;
        });
    }
}
