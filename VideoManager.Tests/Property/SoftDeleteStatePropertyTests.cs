using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for soft delete state setting via DeleteService.
/// **Feature: video-manager-optimization-v2, Property 6: 软删除状态设置**
/// **Validates: Requirements 6.3**
///
/// For any VideoEntry that exists in the database, after performing a soft delete,
/// the record's IsDeleted should be true, DeletedAt should not be null and should be
/// a UTC time, and the record should not be visible through regular queries.
/// </summary>
public class SoftDeleteStatePropertyTests
{
    /// <summary>
    /// Creates a VideoEntry with the given index and seed.
    /// </summary>
    private static VideoEntry CreateVideoEntry(int index, int seed)
    {
        return new VideoEntry
        {
            Title = $"Video_{seed}_{index}",
            FileName = $"video_{seed}_{index}.mp4",
            FilePath = $"/videos/video_{seed}_{index}.mp4",
            FileSize = 1024L * (index + 1),
            Duration = TimeSpan.FromMinutes(index + 1),
            Width = 1920,
            Height = 1080,
            ImportedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false,
            DeletedAt = null
        };
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v2, Property 6: 软删除状态设置**
    /// **Validates: Requirements 6.3**
    ///
    /// For any set of 1-10 VideoEntry records inserted into the database,
    /// picking a random one to soft-delete via DeleteService.DeleteVideoAsync:
    /// 1. The soft-deleted record has IsDeleted=true (via IgnoreQueryFilters)
    /// 2. The soft-deleted record has DeletedAt not null
    /// 3. The soft-deleted record is NOT visible in regular queries
    /// 4. All other (non-deleted) records are still visible in regular queries
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property SoftDeleteShouldSetStateAndHideRecord()
    {
        var countArb = FsCheck.Fluent.Arb.From(FsCheck.Fluent.Gen.Choose(1, 10));
        var indexFractionArb = FsCheck.Fluent.Arb.From(FsCheck.Fluent.Gen.Choose(0, 9999));

        return FsCheck.Fluent.Prop.ForAll(countArb, indexFractionArb, (count, indexFraction) =>
        {
            var seed = Environment.TickCount ^ (count * 31 + indexFraction);
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            try
            {
                var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
                    .UseSqlite(connection)
                    .Options;

                // Phase 1: Insert records
                int targetVideoId;
                var beforeDelete = DateTime.UtcNow;

                using (var context = new VideoManagerDbContext(options))
                {
                    context.Database.EnsureCreated();

                    var entries = new List<VideoEntry>();
                    for (int i = 0; i < count; i++)
                    {
                        entries.Add(CreateVideoEntry(i, seed));
                    }

                    context.VideoEntries.AddRange(entries);
                    context.SaveChanges();

                    // Pick a random video to delete based on indexFraction
                    var allIds = context.VideoEntries.Select(v => v.Id).ToList();
                    var targetIndex = indexFraction % allIds.Count;
                    targetVideoId = allIds[targetIndex];
                }

                // Phase 2: Soft-delete using DeleteService
                using (var context = new VideoManagerDbContext(options))
                {
                    var deleteService = new DeleteService(context, NullLogger<DeleteService>.Instance);
                    var result = deleteService.DeleteVideoAsync(targetVideoId, deleteFile: false, CancellationToken.None)
                        .GetAwaiter().GetResult();

                    if (!result.Success)
                        return false;
                }

                var afterDelete = DateTime.UtcNow;

                // Phase 3: Verify state using a fresh context
                using (var context = new VideoManagerDbContext(options))
                {
                    // Verify 1: The soft-deleted record has IsDeleted=true (via IgnoreQueryFilters)
                    var deletedRecord = context.VideoEntries
                        .IgnoreQueryFilters()
                        .FirstOrDefault(v => v.Id == targetVideoId);

                    if (deletedRecord is null)
                        return false;

                    if (!deletedRecord.IsDeleted)
                        return false;

                    // Verify 2: DeletedAt is not null and is a reasonable UTC time
                    if (deletedRecord.DeletedAt is null)
                        return false;

                    if (deletedRecord.DeletedAt.Value < beforeDelete || deletedRecord.DeletedAt.Value > afterDelete)
                        return false;

                    // Verify 3: The soft-deleted record is NOT visible in regular queries
                    var regularResults = context.VideoEntries.ToList();
                    if (regularResults.Any(v => v.Id == targetVideoId))
                        return false;

                    // Verify 4: All other (non-deleted) records are still visible in regular queries
                    if (regularResults.Count != count - 1)
                        return false;

                    // None of the visible records should be marked as deleted
                    if (regularResults.Any(v => v.IsDeleted))
                        return false;
                }

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
