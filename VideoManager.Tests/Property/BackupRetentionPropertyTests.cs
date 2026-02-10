using System.Globalization;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for backup retention invariants.
/// Tests Property 5: 备份保留数量不变量
///
/// **Feature: video-manager-optimization-v3, Property 5: 备份保留数量不变量**
/// **Validates: Requirements 8.4**
///
/// For any backup cleanup operation:
/// - The number of backup files in the directory does not exceed MaxBackupCount
/// - The retained backup files are always the N newest ones by creation timestamp
/// </summary>
public class BackupRetentionPropertyTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
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
        var dir = Path.Combine(Path.GetTempPath(), $"backup_pbt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    /// <summary>
    /// Creates backup files in the given directory with distinct timestamps.
    /// Returns the list of timestamps used (sorted oldest to newest).
    /// </summary>
    private static List<DateTime> CreateBackupFiles(string backupDir, int count, int seed)
    {
        var rng = new Random(seed);
        var timestamps = new List<DateTime>();

        // Generate distinct timestamps spread across a range
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0);
        for (int i = 0; i < count; i++)
        {
            // Each file gets a unique timestamp by adding i hours + random minutes
            var ts = baseTime.AddHours(i).AddMinutes(rng.Next(0, 59)).AddSeconds(rng.Next(0, 59));
            timestamps.Add(ts);
        }

        // Sort to ensure uniqueness ordering is clear (oldest first)
        timestamps.Sort();

        foreach (var ts in timestamps)
        {
            var fileName = $"{BackupService.BackupFilePrefix}{ts.ToString(BackupService.BackupTimestampFormat, CultureInfo.InvariantCulture)}{BackupService.BackupFileExtension}";
            var filePath = Path.Combine(backupDir, fileName);
            File.WriteAllText(filePath, "test backup content");
        }

        return timestamps;
    }

    /// <summary>
    /// Creates a BackupService instance configured to use the given backup directory.
    /// Uses a file-based SQLite database in a temp directory.
    /// </summary>
    private BackupService CreateBackupService(string backupDir, int maxBackupCount)
    {
        // Create a temp database file for the BackupService constructor
        var dbDir = CreateTempDir();
        var dbPath = Path.Combine(dbDir, "test.db");

        // Create the actual database file so the context can work
        var dbOptions = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        using (var ctx = new VideoManagerDbContext(dbOptions))
        {
            ctx.Database.EnsureCreated();
        }

        // Set up the DbContextFactory mock
        var factoryMock = new Mock<IDbContextFactory<VideoManagerDbContext>>();
        factoryMock.Setup(f => f.CreateDbContext())
            .Returns(() =>
            {
                var opts = new DbContextOptionsBuilder<VideoManagerDbContext>()
                    .UseSqlite($"Data Source={dbPath}")
                    .Options;
                return new VideoManagerDbContext(opts);
            });
        factoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var opts = new DbContextOptionsBuilder<VideoManagerDbContext>()
                    .UseSqlite($"Data Source={dbPath}")
                    .Options;
                return new VideoManagerDbContext(opts);
            });

        var vmOptions = new VideoManagerOptions
        {
            BackupDirectory = backupDir,
            MaxBackupCount = maxBackupCount
        };

        return new BackupService(
            NullLogger<BackupService>.Instance,
            Options.Create(vmOptions),
            factoryMock.Object);
    }

    /// <summary>
    /// Generates a random backup retention scenario as an int array:
    /// [fileCount, maxBackupCount, seed]
    /// fileCount: 1-20 (number of backup files to create)
    /// maxBackupCount: 1-10 (configured retention limit)
    /// seed: used for deterministic file timestamp generation
    /// </summary>
    private static FsCheck.Arbitrary<int[]> BackupRetentionScenarioArb()
    {
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, 99999)),
            arr =>
            {
                var fileCount = arr.Length > 0 ? (arr[0] % 20) + 1 : 5;            // 1-20
                var maxBackupCount = arr.Length > 1 ? (arr[1] % 10) + 1 : 3;       // 1-10
                var seed = arr.Length > 2 ? Math.Abs(arr[2]) + 1 : 1;              // positive seed
                return new int[] { fileCount, maxBackupCount, seed };
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 3));
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v3, Property 5: 备份保留数量不变量 — Count Invariant**
    /// **Validates: Requirements 8.4**
    ///
    /// After CleanupOldBackupsAsync, the number of backup files in the directory
    /// does not exceed the configured MaxBackupCount.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property AfterCleanup_BackupCount_DoesNotExceed_MaxBackupCount()
    {
        return FsCheck.Fluent.Prop.ForAll(BackupRetentionScenarioArb(), config =>
        {
            int fileCount = config[0];
            int maxBackupCount = config[1];
            int seed = config[2];

            var backupDir = CreateTempDir();
            CreateBackupFiles(backupDir, fileCount, seed);

            var service = CreateBackupService(backupDir, maxBackupCount);
            service.CleanupOldBackupsAsync(CancellationToken.None).GetAwaiter().GetResult();

            // Verify: remaining backup files do not exceed MaxBackupCount
            var remainingBackups = service.ListBackups();
            return remainingBackups.Count <= maxBackupCount;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v3, Property 5: 备份保留数量不变量 — Newest Retained**
    /// **Validates: Requirements 8.4**
    ///
    /// After CleanupOldBackupsAsync, the retained backup files are always
    /// the N newest ones (by creation timestamp in the filename).
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property AfterCleanup_RetainedBackups_AreTheNewest()
    {
        return FsCheck.Fluent.Prop.ForAll(BackupRetentionScenarioArb(), config =>
        {
            int fileCount = config[0];
            int maxBackupCount = config[1];
            int seed = config[2];

            var backupDir = CreateTempDir();
            var timestamps = CreateBackupFiles(backupDir, fileCount, seed);

            // Determine which timestamps should be retained (the newest N)
            var expectedRetainedCount = Math.Min(fileCount, maxBackupCount);
            var expectedRetainedTimestamps = timestamps
                .OrderByDescending(t => t)
                .Take(expectedRetainedCount)
                .OrderByDescending(t => t)
                .ToList();

            var service = CreateBackupService(backupDir, maxBackupCount);
            service.CleanupOldBackupsAsync(CancellationToken.None).GetAwaiter().GetResult();

            // Verify: retained backups match the expected newest timestamps
            var remainingBackups = service.ListBackups();

            if (remainingBackups.Count != expectedRetainedCount)
                return false;

            // ListBackups returns sorted newest-first
            for (int i = 0; i < expectedRetainedCount; i++)
            {
                // Compare timestamps (truncated to seconds since that's the filename format)
                var expected = expectedRetainedTimestamps[i];
                var actual = remainingBackups[i].CreatedAt;

                if (expected.Year != actual.Year ||
                    expected.Month != actual.Month ||
                    expected.Day != actual.Day ||
                    expected.Hour != actual.Hour ||
                    expected.Minute != actual.Minute ||
                    expected.Second != actual.Second)
                {
                    return false;
                }
            }

            return true;
        });
    }
}
