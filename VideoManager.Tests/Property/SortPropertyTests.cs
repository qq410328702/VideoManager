using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for sort correctness.
/// </summary>
public class SortPropertyTests
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
    /// Generates a sort test scenario as an int array:
    /// [videoCount, sortFieldIndex, sortDirIndex, seed]
    /// videoCount: 2-20, sortFieldIndex: 0-2 (ImportedAt/Duration/FileSize), sortDirIndex: 0-1 (Asc/Desc)
    /// </summary>
    private static FsCheck.Arbitrary<int[]> SortScenarioArb()
    {
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, 99999)),
            arr =>
            {
                var videoCount = arr.Length > 0 ? (arr[0] % 19) + 2 : 3;       // 2-20 videos
                var sortFieldIndex = arr.Length > 1 ? arr[1] % 3 : 0;          // 0=ImportedAt, 1=Duration, 2=FileSize
                var sortDirIndex = arr.Length > 2 ? arr[2] % 2 : 0;            // 0=Ascending, 1=Descending
                var seed = arr.Length > 3 ? Math.Abs(arr[3]) + 1 : 1;          // positive seed
                return new int[] { videoCount, sortFieldIndex, sortDirIndex, seed };
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 4));
    }

    /// <summary>
    /// **Feature: video-manager-optimization, Property 2: 排序正确性**
    /// **Validates: Requirements 3.2, 3.3**
    ///
    /// For any video list, sort field (Duration/FileSize/ImportedAt) and sort direction
    /// (Ascending/Descending), every pair of adjacent elements in the sorted result
    /// should satisfy the specified ordering relation on the sort field.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property SortOrderCorrectness()
    {
        return FsCheck.Fluent.Prop.ForAll(SortScenarioArb(), config =>
        {
            int videoCount = config[0];
            int sortFieldIndex = config[1];
            int sortDirIndex = config[2];
            int seed = config[3];

            var sortField = sortFieldIndex switch
            {
                0 => SortField.ImportedAt,
                1 => SortField.Duration,
                2 => SortField.FileSize,
                _ => SortField.ImportedAt
            };

            var sortDirection = sortDirIndex switch
            {
                0 => SortDirection.Ascending,
                1 => SortDirection.Descending,
                _ => SortDirection.Ascending
            };

            using var context = CreateInMemoryContext();
            var mockMetrics = new Mock<IMetricsService>();
            mockMetrics.Setup(m => m.StartTimer(It.IsAny<string>())).Returns(new NoOpDisposable());
            var searchService = new SearchService(context, mockMetrics.Object, NullLogger<SearchService>.Instance);
            var ct = CancellationToken.None;

            // --- Setup: create videos with varied properties ---
            var rng = new Random(seed);

            for (int i = 0; i < videoCount; i++)
            {
                var durationMinutes = rng.Next(1, 300);       // 1-299 minutes
                var fileSize = (long)rng.Next(1, 100000);     // varied file sizes
                var importDay = rng.Next(0, 730);             // spread across 2 years

                var video = new VideoEntry
                {
                    Title = $"Video_{seed}_{i}",
                    FileName = $"video_{seed}_{i}.mp4",
                    FilePath = $"/videos/video_{seed}_{i}.mp4",
                    FileSize = fileSize * 1024,
                    Duration = TimeSpan.FromMinutes(durationMinutes),
                    Width = 1920,
                    Height = 1080,
                    ImportedAt = new DateTime(2023, 1, 1).AddDays(importDay),
                    CreatedAt = DateTime.UtcNow
                };

                context.VideoEntries.Add(video);
            }
            context.SaveChanges();

            // --- Execute search with sort criteria (no filters, get all results) ---
            var criteria = new SearchCriteria(
                Keyword: null,
                TagIds: null,
                DateFrom: null,
                DateTo: null,
                DurationMin: null,
                DurationMax: null,
                SortBy: sortField,
                SortDir: sortDirection
            );

            var result = searchService.SearchAsync(criteria, 1, 1000, ct)
                .GetAwaiter().GetResult();

            // --- Verify: all items were returned ---
            if (result.Items.Count != videoCount)
                return false;

            // --- Verify: adjacent elements satisfy the ordering relation ---
            for (int i = 0; i < result.Items.Count - 1; i++)
            {
                var current = result.Items[i];
                var next = result.Items[i + 1];

                bool ordered = (sortField, sortDirection) switch
                {
                    (SortField.ImportedAt, SortDirection.Ascending) =>
                        current.ImportedAt <= next.ImportedAt,
                    (SortField.ImportedAt, SortDirection.Descending) =>
                        current.ImportedAt >= next.ImportedAt,
                    (SortField.Duration, SortDirection.Ascending) =>
                        current.DurationTicks <= next.DurationTicks,
                    (SortField.Duration, SortDirection.Descending) =>
                        current.DurationTicks >= next.DurationTicks,
                    (SortField.FileSize, SortDirection.Ascending) =>
                        current.FileSize <= next.FileSize,
                    (SortField.FileSize, SortDirection.Descending) =>
                        current.FileSize >= next.FileSize,
                    _ => false
                };

                if (!ordered)
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
