using Microsoft.EntityFrameworkCore;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Repositories;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for database transaction rollback behavior.
/// </summary>
public class TransactionPropertyTests
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
    /// Generates a random initial state configuration as an int array
    /// [tagCount, videoCount, seed].
    /// tagCount: 1-5, videoCount: 1-5, seed: positive int for unique naming.
    /// </summary>
    private static FsCheck.Arbitrary<int[]> TransactionConfigArb()
    {
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(1, 99999)),
            arr =>
            {
                var tagCount = arr.Length > 0 ? (arr[0] % 5) + 1 : 1;       // 1-5
                var videoCount = arr.Length > 1 ? (arr[1] % 5) + 1 : 1;     // 1-5
                var seed = arr.Length > 2 ? Math.Abs(arr[2]) + 1 : 1;       // positive seed
                return new int[] { tagCount, videoCount, seed };
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 3));
    }

    /// <summary>
    /// **Feature: video-manager, Property 15: 数据库异常事务回滚**
    /// **Validates: Requirements 8.4**
    ///
    /// For any database operation that causes an exception, the database state
    /// should remain the same as before the operation (transaction rolled back),
    /// with no partial writes.
    ///
    /// Test strategy:
    /// 1. Create initial valid data (tags and videos) and record the state.
    /// 2. Attempt a batch operation within an explicit transaction that includes
    ///    both a valid insert and an invalid insert (duplicate Tag name violating
    ///    the unique constraint).
    /// 3. The transaction should be rolled back on exception.
    /// 4. Verify the database state is identical to the state before the failed operation.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property DatabaseExceptionTransactionRollback()
    {
        return FsCheck.Fluent.Prop.ForAll(TransactionConfigArb(), config =>
        {
            int tagCount = config[0];
            int videoCount = config[1];
            int seed = config[2];

            using var context = CreateInMemoryContext();
            var ct = CancellationToken.None;

            // Step 1: Create initial valid data
            var initialTags = new List<Tag>();
            for (int i = 0; i < tagCount; i++)
            {
                var tag = new Tag { Name = $"Tag_{seed}_{i}" };
                context.Tags.Add(tag);
                initialTags.Add(tag);
            }
            context.SaveChanges();

            var initialVideos = new List<VideoEntry>();
            for (int i = 0; i < videoCount; i++)
            {
                var video = new VideoEntry
                {
                    Title = $"Video_{seed}_{i}",
                    FileName = $"video_{seed}_{i}.mp4",
                    FilePath = $"/videos/video_{seed}_{i}.mp4",
                    FileSize = 1024L * (i + 1),
                    Duration = TimeSpan.FromMinutes(i + 1),
                    Width = 1920,
                    Height = 1080,
                    ImportedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                context.VideoEntries.Add(video);
                initialVideos.Add(video);
            }
            context.SaveChanges();

            // Record the state before the failed operation
            context.ChangeTracker.Clear();
            int tagCountBefore = context.Tags.Count();
            int videoCountBefore = context.VideoEntries.Count();
            var tagNamesBefore = context.Tags.OrderBy(t => t.Name).Select(t => t.Name).ToList();
            var videoTitlesBefore = context.VideoEntries.OrderBy(v => v.Title).Select(v => v.Title).ToList();

            // Step 2: Attempt a batch operation within an explicit transaction
            // that will fail due to a unique constraint violation.
            // We insert a new valid tag AND a duplicate tag in the same transaction.
            bool exceptionCaught = false;
            using (var transaction = context.Database.BeginTransaction())
            {
                try
                {
                    // Valid insert: a new tag with a unique name
                    var newTag = new Tag { Name = $"NewTag_{seed}_valid" };
                    context.Tags.Add(newTag);
                    context.SaveChanges();

                    // Invalid insert: a duplicate tag name (same as the first initial tag)
                    var duplicateTag = new Tag { Name = initialTags[0].Name };
                    context.Tags.Add(duplicateTag);
                    context.SaveChanges(); // This should throw DbUpdateException

                    // If we get here, commit (shouldn't happen)
                    transaction.Commit();
                }
                catch (DbUpdateException)
                {
                    exceptionCaught = true;
                    transaction.Rollback();
                }
            }

            // Detach all tracked entities to ensure fresh queries
            context.ChangeTracker.Clear();

            // Step 3: Verify the database state is unchanged
            int tagCountAfter = context.Tags.Count();
            int videoCountAfter = context.VideoEntries.Count();
            var tagNamesAfter = context.Tags.OrderBy(t => t.Name).Select(t => t.Name).ToList();
            var videoTitlesAfter = context.VideoEntries.OrderBy(v => v.Title).Select(v => v.Title).ToList();

            // The exception must have been caught
            bool exceptionOccurred = exceptionCaught;

            // Tag count should be the same as before the failed operation
            bool tagCountUnchanged = tagCountAfter == tagCountBefore;

            // Video count should be the same as before the failed operation
            bool videoCountUnchanged = videoCountAfter == videoCountBefore;

            // Tag names should be identical
            bool tagNamesUnchanged = tagNamesBefore.SequenceEqual(tagNamesAfter);

            // Video titles should be identical
            bool videoTitlesUnchanged = videoTitlesBefore.SequenceEqual(videoTitlesAfter);

            return exceptionOccurred
                && tagCountUnchanged
                && videoCountUnchanged
                && tagNamesUnchanged
                && videoTitlesUnchanged;
        });
    }
}
