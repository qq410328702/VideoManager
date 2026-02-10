using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for backup restore round-trip consistency.
/// Tests Property 7: 备份恢复往返一致性
///
/// **Feature: video-manager-optimization-v3, Property 7: 备份恢复往返一致性**
/// **Validates: Requirements 10.2**
///
/// For any database state S, after creating a backup B, modifying the database to state S',
/// and restoring from backup B, the database state should be equivalent to S
/// (VideoEntries, Tags, FolderCategories content should match).
/// </summary>
public class BackupRestorePropertyTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        // Clear all SQLite connection pools to release file locks
        SqliteConnection.ClearAllPools();

        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    /// <summary>
    /// Creates a temporary directory and registers it for cleanup.
    /// </summary>
    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"backup_restore_pbt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    /// <summary>
    /// Creates a DbContextOptions for a file-based SQLite database at the given path.
    /// </summary>
    private static DbContextOptionsBuilder<VideoManagerDbContext> CreateDbOptions(string dbPath)
    {
        return new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite($"Data Source={dbPath}");
    }

    /// <summary>
    /// Creates a BackupService instance configured to use the given database and backup directory.
    /// </summary>
    private BackupService CreateBackupService(string dbPath, string backupDir)
    {
        var factoryMock = new Mock<IDbContextFactory<VideoManagerDbContext>>();
        factoryMock.Setup(f => f.CreateDbContext())
            .Returns(() => new VideoManagerDbContext(CreateDbOptions(dbPath).Options));
        factoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new VideoManagerDbContext(CreateDbOptions(dbPath).Options));

        var vmOptions = new VideoManagerOptions
        {
            BackupDirectory = backupDir,
            MaxBackupCount = 50 // High limit so cleanup doesn't interfere
        };

        return new BackupService(
            NullLogger<BackupService>.Instance,
            Options.Create(vmOptions),
            factoryMock.Object);
    }

    /// <summary>
    /// Represents a snapshot of the database state for comparison.
    /// </summary>
    private record DatabaseSnapshot(
        List<(string Title, string FileName, long FileSize)> VideoEntries,
        List<string> TagNames,
        List<string> FolderCategoryNames);

    /// <summary>
    /// Captures the current database state as a snapshot (ignoring soft-deleted entries).
    /// </summary>
    private static DatabaseSnapshot CaptureSnapshot(string dbPath)
    {
        using var ctx = new VideoManagerDbContext(CreateDbOptions(dbPath).Options);

        var videos = ctx.VideoEntries
            .AsNoTracking()
            .OrderBy(v => v.Title).ThenBy(v => v.FileName)
            .Select(v => new { v.Title, v.FileName, v.FileSize })
            .ToList()
            .Select(v => (v.Title, v.FileName, v.FileSize))
            .ToList();

        var tags = ctx.Tags
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => t.Name)
            .ToList();

        var categories = ctx.FolderCategories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => c.Name)
            .ToList();

        return new DatabaseSnapshot(videos, tags, categories);
    }

    /// <summary>
    /// Compares two database snapshots for equivalence.
    /// </summary>
    private static bool SnapshotsAreEqual(DatabaseSnapshot a, DatabaseSnapshot b)
    {
        if (a.VideoEntries.Count != b.VideoEntries.Count)
            return false;
        if (a.TagNames.Count != b.TagNames.Count)
            return false;
        if (a.FolderCategoryNames.Count != b.FolderCategoryNames.Count)
            return false;

        for (int i = 0; i < a.VideoEntries.Count; i++)
        {
            if (a.VideoEntries[i] != b.VideoEntries[i])
                return false;
        }

        for (int i = 0; i < a.TagNames.Count; i++)
        {
            if (a.TagNames[i] != b.TagNames[i])
                return false;
        }

        for (int i = 0; i < a.FolderCategoryNames.Count; i++)
        {
            if (a.FolderCategoryNames[i] != b.FolderCategoryNames[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Populates the database with random VideoEntries, Tags, and FolderCategories.
    /// </summary>
    private static void PopulateDatabase(string dbPath, int videoCount, int tagCount, int categoryCount, Random rng)
    {
        using var ctx = new VideoManagerDbContext(CreateDbOptions(dbPath).Options);

        // Create tags
        var tags = new List<Tag>();
        for (int i = 0; i < tagCount; i++)
        {
            var tag = new Tag { Name = $"Tag_{rng.Next(100000)}_{i}" };
            tags.Add(tag);
            ctx.Tags.Add(tag);
        }

        // Create folder categories
        var categories = new List<FolderCategory>();
        for (int i = 0; i < categoryCount; i++)
        {
            var cat = new FolderCategory { Name = $"Category_{rng.Next(100000)}_{i}" };
            categories.Add(cat);
            ctx.FolderCategories.Add(cat);
        }

        // Create video entries with random associations
        for (int i = 0; i < videoCount; i++)
        {
            var video = new VideoEntry
            {
                Title = $"Video_{rng.Next(100000)}_{i}",
                FileName = $"file_{rng.Next(100000)}_{i}.mp4",
                FilePath = $"/videos/file_{i}.mp4",
                FileSize = rng.Next(1000, 1000000),
                DurationTicks = TimeSpan.FromSeconds(rng.Next(10, 3600)).Ticks,
                Width = 1920,
                Height = 1080,
                ImportedAt = DateTime.Now.AddDays(-rng.Next(1, 365)),
                CreatedAt = DateTime.Now.AddDays(-rng.Next(1, 365))
            };

            // Randomly assign some tags
            if (tags.Count > 0)
            {
                var tagCount2 = rng.Next(0, Math.Min(3, tags.Count) + 1);
                var shuffled = tags.OrderBy(_ => rng.Next()).Take(tagCount2).ToList();
                foreach (var tag in shuffled)
                {
                    video.Tags.Add(tag);
                }
            }

            // Randomly assign some categories
            if (categories.Count > 0)
            {
                var catCount2 = rng.Next(0, Math.Min(2, categories.Count) + 1);
                var shuffled = categories.OrderBy(_ => rng.Next()).Take(catCount2).ToList();
                foreach (var cat in shuffled)
                {
                    video.Categories.Add(cat);
                }
            }

            ctx.VideoEntries.Add(video);
        }

        ctx.SaveChanges();
    }

    /// <summary>
    /// Modifies the database to create a different state S' from the original state S.
    /// Adds new entries, deletes some existing ones, and updates others.
    /// </summary>
    private static void ModifyDatabase(string dbPath, Random rng)
    {
        using var ctx = new VideoManagerDbContext(CreateDbOptions(dbPath).Options);

        // Add a new video entry
        ctx.VideoEntries.Add(new VideoEntry
        {
            Title = $"Modified_Video_{rng.Next(100000)}",
            FileName = $"modified_{rng.Next(100000)}.mp4",
            FilePath = "/videos/modified.mp4",
            FileSize = rng.Next(1000, 500000),
            DurationTicks = TimeSpan.FromSeconds(60).Ticks,
            Width = 1280,
            Height = 720,
            ImportedAt = DateTime.Now,
            CreatedAt = DateTime.Now
        });

        // Add a new tag
        ctx.Tags.Add(new Tag { Name = $"ModifiedTag_{rng.Next(100000)}" });

        // Add a new category
        ctx.FolderCategories.Add(new FolderCategory { Name = $"ModifiedCat_{rng.Next(100000)}" });

        // Delete the first video entry if any exist
        var firstVideo = ctx.VideoEntries.FirstOrDefault();
        if (firstVideo != null)
        {
            ctx.VideoEntries.Remove(firstVideo);
        }

        // Update a video title if any remain
        var anotherVideo = ctx.VideoEntries.Skip(1).FirstOrDefault();
        if (anotherVideo != null)
        {
            anotherVideo.Title = $"Updated_{rng.Next(100000)}";
        }

        ctx.SaveChanges();
    }

    /// <summary>
    /// FsCheck arbitrary that generates backup restore test scenarios as int arrays:
    /// [videoCount, tagCount, categoryCount, seed]
    /// videoCount: 1-10
    /// tagCount: 0-5
    /// categoryCount: 0-5
    /// seed: for deterministic random generation
    /// </summary>
    private static FsCheck.Arbitrary<int[]> BackupRestoreScenarioArb()
    {
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, 99999)),
            arr =>
            {
                var videoCount = arr.Length > 0 ? (arr[0] % 10) + 1 : 3;       // 1-10
                var tagCount = arr.Length > 1 ? arr[1] % 6 : 2;                // 0-5
                var categoryCount = arr.Length > 2 ? arr[2] % 6 : 2;           // 0-5
                var seed = arr.Length > 3 ? Math.Abs(arr[3]) + 1 : 1;          // positive seed
                return new int[] { videoCount, tagCount, categoryCount, seed };
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 4));
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v3, Property 7: 备份恢复往返一致性**
    /// **Validates: Requirements 10.2**
    ///
    /// For any database state S, after creating backup B, modifying the database to state S',
    /// and restoring from backup B, the database state should be equivalent to S.
    /// VideoEntries, Tags, and FolderCategories content should all match the original state.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property BackupRestore_RoundTrip_PreservesOriginalState()
    {
        return FsCheck.Fluent.Prop.ForAll(BackupRestoreScenarioArb(), config =>
        {
            int videoCount = config[0];
            int tagCount = config[1];
            int categoryCount = config[2];
            int seed = config[3];
            var rng = new Random(seed);

            // Set up temp directories for database and backups
            var dbDir = CreateTempDir();
            var backupDir = CreateTempDir();
            var dbPath = Path.Combine(dbDir, "test.db");

            // Create and initialize the database
            using (var ctx = new VideoManagerDbContext(CreateDbOptions(dbPath).Options))
            {
                ctx.Database.EnsureCreated();
            }

            // Step 1: Populate database with random state S
            PopulateDatabase(dbPath, videoCount, tagCount, categoryCount, rng);

            // Capture state S
            var stateS = CaptureSnapshot(dbPath);

            // Step 2: Create backup B
            var service = CreateBackupService(dbPath, backupDir);
            var backupPath = service.CreateBackupAsync(CancellationToken.None).GetAwaiter().GetResult();

            // Wait to ensure the next backup (safety backup during restore) gets a different timestamp
            // BackupService uses yyyyMMdd_HHmmss format, so we need at least 1 second gap
            Thread.Sleep(1100);

            // Step 3: Modify database to state S'
            ModifyDatabase(dbPath, rng);

            // Verify the database was actually modified (state S' != state S)
            var stateSPrime = CaptureSnapshot(dbPath);
            // S' should differ from S (we added, deleted, and updated entries)

            // Step 4: Restore from backup B
            // Clear pools before restore to avoid locked file issues
            SqliteConnection.ClearAllPools();
            service.RestoreFromBackupAsync(backupPath, CancellationToken.None).GetAwaiter().GetResult();

            // Step 5: Verify database state matches original state S
            // Clear pools again to ensure fresh connections after restore
            SqliteConnection.ClearAllPools();
            var restoredState = CaptureSnapshot(dbPath);

            return SnapshotsAreEqual(stateS, restoredState);
        });
    }
}
