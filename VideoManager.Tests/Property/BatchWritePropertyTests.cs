using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Repositories;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for batch write consistency via AddRangeAsync.
/// **Feature: video-manager-optimization-v2, Property 4: 批量写入一致性**
/// **Validates: Requirements 5.2, 5.3**
///
/// For any VideoEntry list (1-50 items), after batch writing via AddRangeAsync,
/// the database should contain all written records and the record count should
/// equal the list length.
/// </summary>
public class BatchWritePropertyTests
{
    /// <summary>
    /// Creates a VideoEntry with unique title and filename based on index and seed.
    /// </summary>
    private static VideoEntry CreateVideoEntry(int index, int seed)
    {
        return new VideoEntry
        {
            Title = $"BatchVideo_{seed}_{index}",
            FileName = $"batch_video_{seed}_{index}.mp4",
            FilePath = $"/videos/batch_video_{seed}_{index}.mp4",
            FileSize = 1024L * (index + 1),
            Duration = TimeSpan.FromMinutes(index + 1),
            Width = 1920,
            Height = 1080,
            ImportedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v2, Property 4: 批量写入一致性**
    /// **Validates: Requirements 5.2, 5.3**
    ///
    /// For any count n in [1, 50], creating n VideoEntry objects with unique titles
    /// and filenames and batch-writing them via AddRangeAsync should result in:
    /// 1. The database containing exactly n records
    /// 2. All records existing with correct Title and FileName data
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property BatchWriteShouldPersistAllRecordsConsistently()
    {
        // Generate a random count n in [1, 50]
        var countArb = FsCheck.Fluent.Arb.From(FsCheck.Fluent.Gen.Choose(1, 50));

        return FsCheck.Fluent.Prop.ForAll(countArb, n =>
        {
            var seed = Environment.TickCount ^ n;
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            try
            {
                var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
                    .UseSqlite(connection)
                    .Options;

                using var context = new VideoManagerDbContext(options);
                context.Database.EnsureCreated();

                var repository = new VideoRepository(context);

                // Create n VideoEntry objects with unique titles and filenames
                var entries = new List<VideoEntry>();
                for (int i = 0; i < n; i++)
                {
                    entries.Add(CreateVideoEntry(i, seed));
                }

                // Batch write via AddRangeAsync
                repository.AddRangeAsync(entries, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Clear the change tracker to force reading from DB
                context.ChangeTracker.Clear();

                // Verify: database contains exactly n records
                var dbCount = context.VideoEntries.Count();
                if (dbCount != n)
                    return false;

                // Verify: all records exist with correct data
                var storedEntries = context.VideoEntries
                    .OrderBy(v => v.Title)
                    .ToList();

                if (storedEntries.Count != n)
                    return false;

                // Verify each entry has a unique ID (proves all were persisted individually)
                var uniqueIds = storedEntries.Select(v => v.Id).Distinct().Count();
                if (uniqueIds != n)
                    return false;

                // Verify titles and filenames match what we wrote
                var expectedTitles = entries
                    .Select(e => e.Title)
                    .OrderBy(t => t)
                    .ToList();
                var actualTitles = storedEntries
                    .Select(e => e.Title)
                    .ToList(); // Already ordered by Title

                if (!expectedTitles.SequenceEqual(actualTitles))
                    return false;

                var expectedFileNames = entries
                    .Select(e => e.FileName)
                    .OrderBy(f => f)
                    .ToList();
                var actualFileNames = storedEntries
                    .Select(e => e.FileName)
                    .OrderBy(f => f)
                    .ToList();

                if (!expectedFileNames.SequenceEqual(actualFileNames))
                    return false;

                return true;
            }
            finally
            {
                connection.Close();
                connection.Dispose();
            }
        });
    }
}
