using Microsoft.EntityFrameworkCore;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Repositories;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for pagination query correctness.
/// </summary>
public class PaginationPropertyTests
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
    /// Generates a random pagination configuration as an int array [totalRecords, page, pageSize].
    /// totalRecords: 0-50, page: 1-10, pageSize: 1-20
    /// </summary>
    private static FsCheck.Arbitrary<int[]> PaginationConfigArb()
    {
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, 99999)),
            arr =>
            {
                var totalRecords = arr.Length > 0 ? arr[0] % 51 : 0;          // 0-50
                var page = arr.Length > 1 ? (arr[1] % 10) + 1 : 1;           // 1-10
                var pageSize = arr.Length > 2 ? (arr[2] % 20) + 1 : 1;       // 1-20
                return new int[] { totalRecords, page, pageSize };
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 3));
    }

    /// <summary>
    /// **Feature: video-manager, Property 14: 分页查询正确性**
    /// **Validates: Requirements 7.2**
    ///
    /// For any page number and page size, the returned record count should not
    /// exceed the page size, and TotalCount should equal the total number of
    /// records in the database.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property PaginationQueryCorrectness()
    {
        return FsCheck.Fluent.Prop.ForAll(PaginationConfigArb(), config =>
        {
            int totalRecords = config[0];
            int page = config[1];
            int pageSize = config[2];

            using var context = CreateInMemoryContext();
            var videoRepo = new VideoRepository(context);
            var ct = CancellationToken.None;

            // Insert the generated number of VideoEntry records
            for (int i = 0; i < totalRecords; i++)
            {
                var video = new VideoEntry
                {
                    Title = $"Video_{i}",
                    FileName = $"video_{i}.mp4",
                    FilePath = $"/videos/video_{i}.mp4",
                    FileSize = 1024L * (i + 1),
                    Duration = TimeSpan.FromMinutes(i + 1),
                    Width = 1920,
                    Height = 1080,
                    ImportedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                videoRepo.AddAsync(video, ct).GetAwaiter().GetResult();
            }

            // Call GetPagedAsync with the generated page and pageSize
            var result = videoRepo.GetPagedAsync(page, pageSize, ct).GetAwaiter().GetResult();

            // Property 1: TotalCount should equal the total number of records in the database
            bool totalCountCorrect = result.TotalCount == totalRecords;

            // Property 2: The returned record count should not exceed the page size
            bool itemCountWithinPageSize = result.Items.Count <= pageSize;

            // Property 3: Verify the exact expected item count based on pagination math
            int expectedItemCount;
            int totalPages = totalRecords == 0 ? 0 : (int)Math.Ceiling((double)totalRecords / pageSize);
            if (page > totalPages)
            {
                expectedItemCount = 0;
            }
            else
            {
                int remaining = totalRecords - (page - 1) * pageSize;
                expectedItemCount = Math.Min(remaining, pageSize);
            }
            bool exactItemCountCorrect = result.Items.Count == expectedItemCount;

            // Property 4: Page and PageSize in the result should match the request
            bool pageMetadataCorrect = result.Page == page && result.PageSize == pageSize;

            return totalCountCorrect && itemCountWithinPageSize && exactItemCountCorrect && pageMetadataCorrect;
        });
    }
}
