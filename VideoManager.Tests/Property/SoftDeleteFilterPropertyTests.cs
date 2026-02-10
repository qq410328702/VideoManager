using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VideoManager.Data;
using VideoManager.Models;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for soft delete global query filter.
/// **Feature: video-manager-optimization-v2, Property 5: 软删除全局过滤器**
/// **Validates: Requirements 6.2**
///
/// For any database state containing N active (non-deleted) records and M soft-deleted records,
/// a regular query (without IgnoreQueryFilters) should return exactly N records,
/// and none of the returned records should have IsDeleted=true.
/// </summary>
public class SoftDeleteFilterPropertyTests
{
    /// <summary>
    /// Creates a VideoEntry with the given index, seed, and soft-delete state.
    /// </summary>
    private static VideoEntry CreateVideoEntry(int index, int seed, bool isDeleted)
    {
        return new VideoEntry
        {
            Title = $"Video_{seed}_{index}_{(isDeleted ? "deleted" : "active")}",
            FileName = $"video_{seed}_{index}.mp4",
            FilePath = $"/videos/video_{seed}_{index}.mp4",
            FileSize = 1024L * (index + 1),
            Duration = TimeSpan.FromMinutes(index + 1),
            Width = 1920,
            Height = 1080,
            ImportedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? DateTime.UtcNow : null
        };
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v2, Property 5: 软删除全局过滤器**
    /// **Validates: Requirements 6.2**
    ///
    /// For any activeCount (N) in [0, 20] and deletedCount (M) in [0, 20],
    /// inserting N active VideoEntry records (IsDeleted=false) and M soft-deleted records
    /// (IsDeleted=true, DeletedAt set) into the database, a regular query (without
    /// IgnoreQueryFilters) should return exactly N records, and none of the returned
    /// records should have IsDeleted=true.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property SoftDeleteFilterShouldExcludeDeletedRecords()
    {
        var activeCountArb = FsCheck.Fluent.Arb.From(FsCheck.Fluent.Gen.Choose(0, 20));
        var deletedCountArb = FsCheck.Fluent.Arb.From(FsCheck.Fluent.Gen.Choose(0, 20));

        return FsCheck.Fluent.Prop.ForAll(activeCountArb, deletedCountArb, (activeCount, deletedCount) =>
        {
            var seed = Environment.TickCount ^ (activeCount * 31 + deletedCount);
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            try
            {
                var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
                    .UseSqlite(connection)
                    .Options;

                using var context = new VideoManagerDbContext(options);
                context.Database.EnsureCreated();

                // Insert N active records (IsDeleted=false)
                for (int i = 0; i < activeCount; i++)
                {
                    context.VideoEntries.Add(CreateVideoEntry(i, seed, isDeleted: false));
                }

                // Insert M soft-deleted records (IsDeleted=true, DeletedAt set)
                // The global query filter only affects queries, not inserts,
                // so we can directly add entities with IsDeleted=true.
                for (int i = 0; i < deletedCount; i++)
                {
                    context.VideoEntries.Add(CreateVideoEntry(activeCount + i, seed, isDeleted: true));
                }

                context.SaveChanges();
                context.ChangeTracker.Clear();

                // Regular query (with global query filter applied) should return exactly N records
                var regularQueryResults = context.VideoEntries.ToList();

                if (regularQueryResults.Count != activeCount)
                    return false;

                // None of the returned records should have IsDeleted=true
                if (regularQueryResults.Any(v => v.IsDeleted))
                    return false;

                // Verify via IgnoreQueryFilters that all N+M records actually exist in the database
                var allRecords = context.VideoEntries.IgnoreQueryFilters().ToList();
                if (allRecords.Count != activeCount + deletedCount)
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
