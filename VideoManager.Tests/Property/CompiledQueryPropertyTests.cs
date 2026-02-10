using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for compiled query equivalence.
/// Tests Property 2: 编译查询等价性
///
/// **Feature: video-manager-optimization-v3, Property 2: 编译查询等价性**
/// **Validates: Requirements 4.1, 4.3**
///
/// For any valid SearchCriteria (with any keyword, tag ID list, date range, duration range),
/// the result set returned by compiled queries is identical in content and order to the
/// result set returned by dynamic LINQ queries.
/// </summary>
public class CompiledQueryPropertyTests
{
    /// <summary>
    /// Creates an in-memory SQLite context for testing.
    /// Uses a real SQLite database (in-memory mode) so that EF Core compiled queries
    /// and EF.Functions.Like work correctly.
    /// </summary>
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
    /// Generates a random compiled query test scenario as an int array:
    /// [videoCount, keywordIndex, page, pageSize, seed]
    /// videoCount: 2-15 videos
    /// keywordIndex: index into keyword pool (0-4)
    /// page: 1-3
    /// pageSize: 2-10
    /// seed: used to deterministically generate video data
    /// </summary>
    private static FsCheck.Arbitrary<int[]> CompiledQueryScenarioArb()
    {
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, 99999)),
            arr =>
            {
                var videoCount = arr.Length > 0 ? (arr[0] % 14) + 2 : 5;       // 2-15 videos
                var keywordIndex = arr.Length > 1 ? arr[1] % 5 : 0;            // 0-4 keyword index
                var page = arr.Length > 2 ? (arr[2] % 3) + 1 : 1;             // 1-3
                var pageSize = arr.Length > 3 ? (arr[3] % 9) + 2 : 5;         // 2-10
                var seed = arr.Length > 4 ? Math.Abs(arr[4]) + 1 : 1;         // positive seed
                return new int[] { videoCount, keywordIndex, page, pageSize, seed };
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 5));
    }

    /// <summary>
    /// Generates a random integration test scenario as an int array:
    /// [videoCount, scenarioType, keywordIndex, page, pageSize, seed]
    /// scenarioType: 0 = keyword search (compiled path), 1 = no-filter default paging (compiled path)
    /// </summary>
    private static FsCheck.Arbitrary<int[]> IntegrationScenarioArb()
    {
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, 99999)),
            arr =>
            {
                var videoCount = arr.Length > 0 ? (arr[0] % 14) + 2 : 5;       // 2-15
                var scenarioType = arr.Length > 1 ? arr[1] % 2 : 0;            // 0 or 1
                var keywordIndex = arr.Length > 2 ? arr[2] % 5 : 0;            // 0-4
                var page = arr.Length > 3 ? (arr[3] % 3) + 1 : 1;             // 1-3
                var pageSize = arr.Length > 4 ? (arr[4] % 9) + 2 : 5;         // 2-10
                var seed = arr.Length > 5 ? Math.Abs(arr[5]) + 1 : 1;         // positive seed
                return new int[] { videoCount, scenarioType, keywordIndex, page, pageSize, seed };
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 6));
    }

    private static readonly string[] Keywords = { "Alpha", "Beta", "Gamma", "Delta", "Epsilon" };

    /// <summary>
    /// Seeds the database with random video entries for testing.
    /// Returns the list of created videos.
    /// </summary>
    private static List<VideoEntry> SeedVideos(VideoManagerDbContext context, int videoCount, int seed)
    {
        var rng = new Random(seed);
        var videos = new List<VideoEntry>();

        for (int i = 0; i < videoCount; i++)
        {
            var kw = Keywords[rng.Next(Keywords.Length)];
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
                ImportedAt = new DateTime(2024, 1, 1).AddDays(importDay - 1).AddHours(i),
                CreatedAt = DateTime.UtcNow
            };

            context.VideoEntries.Add(video);
            videos.Add(video);
        }
        context.SaveChanges();
        return videos;
    }

    /// <summary>
    /// Executes a dynamic LINQ query that mirrors the compiled query logic,
    /// to serve as the reference implementation for comparison.
    /// </summary>
    private static PagedResult<VideoEntry> ExecuteDynamicKeywordQuery(
        VideoManagerDbContext context, string keyword, int page, int pageSize)
    {
        int skip = (page - 1) * pageSize;

        var query = context.VideoEntries
            .AsNoTracking()
            .Include(v => v.Tags)
            .Include(v => v.Categories)
            .Where(v => EF.Functions.Like(v.Title, "%" + keyword + "%") ||
                        (v.Description != null && EF.Functions.Like(v.Description, "%" + keyword + "%")))
            .OrderByDescending(v => v.ImportedAt);

        var totalCount = query.Count();
        var items = query.Skip(skip).Take(pageSize).ToList();

        return new PagedResult<VideoEntry>(items, totalCount, page, pageSize);
    }

    /// <summary>
    /// Executes a dynamic LINQ query for default paged listing (no filters),
    /// to serve as the reference implementation for comparison.
    /// </summary>
    private static PagedResult<VideoEntry> ExecuteDynamicPagedDefaultQuery(
        VideoManagerDbContext context, int page, int pageSize)
    {
        int skip = (page - 1) * pageSize;

        var query = context.VideoEntries
            .AsNoTracking()
            .Include(v => v.Tags)
            .Include(v => v.Categories)
            .OrderByDescending(v => v.ImportedAt);

        var totalCount = query.Count();
        var items = query.Skip(skip).Take(pageSize).ToList();

        return new PagedResult<VideoEntry>(items, totalCount, page, pageSize);
    }

    /// <summary>
    /// Helper to collect IAsyncEnumerable into a List synchronously.
    /// </summary>
    private static List<T> ToList<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        var enumerator = source.GetAsyncEnumerator();
        try
        {
            while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
            {
                list.Add(enumerator.Current);
            }
        }
        finally
        {
            enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        return list;
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v3, Property 2: 编译查询等价性 — SearchByKeyword Equivalence**
    /// **Validates: Requirements 4.1**
    ///
    /// For any keyword from the keyword pool and any valid pagination parameters,
    /// the compiled query SearchByKeyword returns the same result set (same IDs in same order)
    /// as the equivalent dynamic LINQ query.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property CompiledSearchByKeyword_MatchesDynamicQuery()
    {
        return FsCheck.Fluent.Prop.ForAll(CompiledQueryScenarioArb(), config =>
        {
            int videoCount = config[0];
            int keywordIndex = config[1];
            int page = config[2];
            int pageSize = config[3];
            int seed = config[4];

            string keyword = Keywords[keywordIndex];
            int skip = (page - 1) * pageSize;

            using var context = CreateInMemoryContext();
            SeedVideos(context, videoCount, seed);

            // Execute compiled query
            var compiledItems = ToList(
                CompiledQueries.SearchByKeyword(context, keyword, skip, pageSize));

            // Clear change tracker to avoid entity tracking interference
            context.ChangeTracker.Clear();

            // Execute dynamic LINQ query as reference
            var dynamicResult = ExecuteDynamicKeywordQuery(context, keyword, page, pageSize);

            // Verify: same number of items
            if (compiledItems.Count != dynamicResult.Items.Count)
                return false;

            // Verify: same IDs in same order
            for (int i = 0; i < compiledItems.Count; i++)
            {
                if (compiledItems[i].Id != dynamicResult.Items[i].Id)
                    return false;
            }

            // Verify: same content (Title, Description, ImportedAt)
            for (int i = 0; i < compiledItems.Count; i++)
            {
                var compiled = compiledItems[i];
                var dynamic_ = dynamicResult.Items[i];

                if (compiled.Title != dynamic_.Title)
                    return false;
                if (compiled.Description != dynamic_.Description)
                    return false;
                if (compiled.ImportedAt != dynamic_.ImportedAt)
                    return false;
                if (compiled.DurationTicks != dynamic_.DurationTicks)
                    return false;
                if (compiled.FileSize != dynamic_.FileSize)
                    return false;
            }

            return true;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v3, Property 2: 编译查询等价性 — GetPagedDefault Equivalence**
    /// **Validates: Requirements 4.3**
    ///
    /// For any valid pagination parameters, the compiled query GetPagedDefault returns
    /// the same result set (same IDs in same order) as the equivalent dynamic LINQ query
    /// with no filters and default sort (ImportedAt Descending).
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property CompiledGetPagedDefault_MatchesDynamicQuery()
    {
        return FsCheck.Fluent.Prop.ForAll(CompiledQueryScenarioArb(), config =>
        {
            int videoCount = config[0];
            int page = config[2];
            int pageSize = config[3];
            int seed = config[4];

            int skip = (page - 1) * pageSize;

            using var context = CreateInMemoryContext();
            SeedVideos(context, videoCount, seed);

            // Execute compiled query
            var compiledItems = ToList(
                CompiledQueries.GetPagedDefault(context, skip, pageSize));

            // Clear change tracker to avoid entity tracking interference
            context.ChangeTracker.Clear();

            // Execute dynamic LINQ query as reference
            var dynamicResult = ExecuteDynamicPagedDefaultQuery(context, page, pageSize);

            // Verify: same number of items
            if (compiledItems.Count != dynamicResult.Items.Count)
                return false;

            // Verify: same IDs in same order
            for (int i = 0; i < compiledItems.Count; i++)
            {
                if (compiledItems[i].Id != dynamicResult.Items[i].Id)
                    return false;
            }

            // Verify: same content (Title, Description, ImportedAt, DurationTicks, FileSize)
            for (int i = 0; i < compiledItems.Count; i++)
            {
                var compiled = compiledItems[i];
                var dynamic_ = dynamicResult.Items[i];

                if (compiled.Title != dynamic_.Title)
                    return false;
                if (compiled.Description != dynamic_.Description)
                    return false;
                if (compiled.ImportedAt != dynamic_.ImportedAt)
                    return false;
                if (compiled.DurationTicks != dynamic_.DurationTicks)
                    return false;
                if (compiled.FileSize != dynamic_.FileSize)
                    return false;
            }

            return true;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v3, Property 2: 编译查询等价性 — SearchService Integration Equivalence**
    /// **Validates: Requirements 4.1, 4.3**
    ///
    /// For any SearchCriteria that hits a compiled query fast path (pure keyword search
    /// or no-filter default paging), the SearchService returns the same results as when
    /// the dynamic LINQ path is used. This verifies the integration of compiled queries
    /// within the SearchService.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property SearchService_CompiledPathMatchesDynamicPath()
    {
        return FsCheck.Fluent.Prop.ForAll(IntegrationScenarioArb(), config =>
        {
            int videoCount = config[0];
            int scenarioType = config[1];
            int keywordIndex = config[2];
            int page = config[3];
            int pageSize = config[4];
            int seed = config[5];

            using var context = CreateInMemoryContext();
            SeedVideos(context, videoCount, seed);

            var mockMetrics = new Mock<IMetricsService>();
            mockMetrics.Setup(m => m.StartTimer(It.IsAny<string>())).Returns(new NoOpDisposable());
            var searchService = new SearchService(context, mockMetrics.Object, NullLogger<SearchService>.Instance);
            var ct = CancellationToken.None;

            SearchCriteria criteria;
            if (scenarioType == 0)
            {
                // Pure keyword search with default sort — hits compiled SearchByKeyword path
                criteria = new SearchCriteria(
                    Keyword: Keywords[keywordIndex],
                    TagIds: null,
                    DateFrom: null,
                    DateTo: null,
                    DurationMin: null,
                    DurationMax: null,
                    SortBy: SortField.ImportedAt,
                    SortDir: SortDirection.Descending);
            }
            else
            {
                // No filters, default sort — hits compiled GetPagedDefault path
                criteria = new SearchCriteria(
                    Keyword: null,
                    TagIds: null,
                    DateFrom: null,
                    DateTo: null,
                    DurationMin: null,
                    DurationMax: null,
                    SortBy: SortField.ImportedAt,
                    SortDir: SortDirection.Descending);
            }

            // Execute via SearchService (which uses compiled queries internally)
            var searchResult = searchService.SearchAsync(criteria, page, pageSize, ct)
                .GetAwaiter().GetResult();

            // Clear change tracker
            context.ChangeTracker.Clear();

            // Execute reference dynamic LINQ query directly
            PagedResult<VideoEntry> referenceResult;
            if (scenarioType == 0)
            {
                referenceResult = ExecuteDynamicKeywordQuery(
                    context, criteria.Keyword!.Trim(), page, pageSize);
            }
            else
            {
                referenceResult = ExecuteDynamicPagedDefaultQuery(context, page, pageSize);
            }

            // Verify: total counts match
            if (searchResult.TotalCount != referenceResult.TotalCount)
                return false;

            // Verify: same number of items
            if (searchResult.Items.Count != referenceResult.Items.Count)
                return false;

            // Verify: same IDs in same order
            for (int i = 0; i < searchResult.Items.Count; i++)
            {
                if (searchResult.Items[i].Id != referenceResult.Items[i].Id)
                    return false;
            }

            // Verify: same content
            for (int i = 0; i < searchResult.Items.Count; i++)
            {
                var actual = searchResult.Items[i];
                var expected = referenceResult.Items[i];

                if (actual.Title != expected.Title)
                    return false;
                if (actual.Description != expected.Description)
                    return false;
                if (actual.ImportedAt != expected.ImportedAt)
                    return false;
                if (actual.DurationTicks != expected.DurationTicks)
                    return false;
                if (actual.FileSize != expected.FileSize)
                    return false;
            }

            return true;
        });
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
