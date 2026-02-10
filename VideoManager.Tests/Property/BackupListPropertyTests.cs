using System.Globalization;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for backup list completeness.
/// Tests Property 6: 备份列表完整性
///
/// **Feature: video-manager-optimization-v3, Property 6: 备份列表完整性**
/// **Validates: Requirements 10.1**
///
/// For any set of backup files in the backup directory, ListBackups returns a list
/// that includes all files matching the naming pattern, and each BackupInfo's
/// CreatedAt and FileSizeBytes are consistent with the file system metadata.
/// </summary>
public class BackupListPropertyTests : IDisposable
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
        var dir = Path.Combine(Path.GetTempPath(), $"backup_list_pbt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    /// <summary>
    /// Information about a backup file created during testing.
    /// </summary>
    private record CreatedBackupFile(string FilePath, DateTime Timestamp, long FileSize);

    /// <summary>
    /// Creates backup files with the correct naming pattern in the given directory.
    /// Each file gets random content of varying size.
    /// Returns the list of created backup file info.
    /// </summary>
    private static List<CreatedBackupFile> CreateMatchingBackupFiles(string backupDir, int count, Random rng)
    {
        var created = new List<CreatedBackupFile>();
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0);

        for (int i = 0; i < count; i++)
        {
            // Generate a unique timestamp for each file
            var ts = baseTime.AddHours(i * 2).AddMinutes(rng.Next(0, 59)).AddSeconds(rng.Next(0, 59));
            var fileName = $"{BackupService.BackupFilePrefix}{ts.ToString(BackupService.BackupTimestampFormat, CultureInfo.InvariantCulture)}{BackupService.BackupFileExtension}";
            var filePath = Path.Combine(backupDir, fileName);

            // Write random content of varying size (10 to 500 bytes)
            var contentSize = rng.Next(10, 501);
            var content = new byte[contentSize];
            rng.NextBytes(content);
            File.WriteAllBytes(filePath, content);

            var fileInfo = new FileInfo(filePath);
            created.Add(new CreatedBackupFile(filePath, ts, fileInfo.Length));
        }

        return created;
    }

    /// <summary>
    /// Creates non-matching files (files that should NOT appear in ListBackups results).
    /// </summary>
    private static void CreateNonMatchingFiles(string backupDir, int count, Random rng)
    {
        var nonMatchingNames = new[]
        {
            "random.txt",
            "other.db",
            "videomanager.db",
            "backup_20240101.db",
            "videomanager_20240101_120000.db",
            "notes.log",
            "config.json",
            "videomanager_backup_.db",
            "videomanager_backup_invalid.db",
            "test_backup_20240101_120000.db",
            "videomanager_backup_2024.db",
            "readme.md",
            "data.sqlite",
            "videomanager_backup_abcdefgh_ijklmn.db",
            "videomanager_backup_99999999_999999.db"
        };

        for (int i = 0; i < count && i < nonMatchingNames.Length; i++)
        {
            var filePath = Path.Combine(backupDir, nonMatchingNames[i]);
            var contentSize = rng.Next(5, 100);
            var content = new byte[contentSize];
            rng.NextBytes(content);
            File.WriteAllBytes(filePath, content);
        }
    }

    /// <summary>
    /// Creates a BackupService instance configured to use the given backup directory.
    /// </summary>
    private BackupService CreateBackupService(string backupDir)
    {
        var dbDir = CreateTempDir();
        var dbPath = Path.Combine(dbDir, "test.db");

        var dbOptions = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        using (var ctx = new VideoManagerDbContext(dbOptions))
        {
            ctx.Database.EnsureCreated();
        }

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
            MaxBackupCount = 100 // High limit so cleanup doesn't interfere
        };

        return new BackupService(
            NullLogger<BackupService>.Instance,
            Options.Create(vmOptions),
            factoryMock.Object);
    }

    /// <summary>
    /// FsCheck arbitrary that generates backup list test scenarios as int arrays:
    /// [matchingFileCount, nonMatchingFileCount, seed]
    /// matchingFileCount: 0-15 (number of valid backup files)
    /// nonMatchingFileCount: 0-10 (number of non-matching files)
    /// seed: for deterministic random generation
    /// </summary>
    private static FsCheck.Arbitrary<int[]> BackupListScenarioArb()
    {
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, 99999)),
            arr =>
            {
                var matchingCount = arr.Length > 0 ? arr[0] % 16 : 3;              // 0-15
                var nonMatchingCount = arr.Length > 1 ? arr[1] % 11 : 2;           // 0-10
                var seed = arr.Length > 2 ? Math.Abs(arr[2]) + 1 : 1;             // positive seed
                return new int[] { matchingCount, nonMatchingCount, seed };
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 3));
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v3, Property 6: 备份列表完整性 — Count Matches**
    /// **Validates: Requirements 10.1**
    ///
    /// ListBackups returns exactly the number of files matching the backup naming pattern,
    /// excluding any non-matching files in the same directory.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property ListBackups_ReturnsExactCount_OfMatchingFiles()
    {
        return FsCheck.Fluent.Prop.ForAll(BackupListScenarioArb(), config =>
        {
            int matchingCount = config[0];
            int nonMatchingCount = config[1];
            int seed = config[2];
            var rng = new Random(seed);

            var backupDir = CreateTempDir();
            CreateMatchingBackupFiles(backupDir, matchingCount, rng);
            CreateNonMatchingFiles(backupDir, nonMatchingCount, rng);

            var service = CreateBackupService(backupDir);
            var backups = service.ListBackups();

            return backups.Count == matchingCount;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v3, Property 6: 备份列表完整性 — CreatedAt Consistency**
    /// **Validates: Requirements 10.1**
    ///
    /// Each BackupInfo's CreatedAt timestamp matches the timestamp parsed from the backup filename.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property ListBackups_CreatedAt_MatchesFilenameTimestamp()
    {
        return FsCheck.Fluent.Prop.ForAll(BackupListScenarioArb(), config =>
        {
            int matchingCount = config[0];
            int nonMatchingCount = config[1];
            int seed = config[2];
            var rng = new Random(seed);

            if (matchingCount == 0)
                return true; // Nothing to verify for empty case

            var backupDir = CreateTempDir();
            var createdFiles = CreateMatchingBackupFiles(backupDir, matchingCount, rng);
            CreateNonMatchingFiles(backupDir, nonMatchingCount, rng);

            var service = CreateBackupService(backupDir);
            var backups = service.ListBackups();

            // Build a lookup of expected timestamps by file path
            var expectedByPath = createdFiles.ToDictionary(f => f.FilePath, f => f.Timestamp);

            foreach (var backup in backups)
            {
                if (!expectedByPath.TryGetValue(backup.FilePath, out var expectedTimestamp))
                    return false; // Unexpected file in results

                // Compare timestamp components (truncated to seconds, matching filename format)
                if (expectedTimestamp.Year != backup.CreatedAt.Year ||
                    expectedTimestamp.Month != backup.CreatedAt.Month ||
                    expectedTimestamp.Day != backup.CreatedAt.Day ||
                    expectedTimestamp.Hour != backup.CreatedAt.Hour ||
                    expectedTimestamp.Minute != backup.CreatedAt.Minute ||
                    expectedTimestamp.Second != backup.CreatedAt.Second)
                {
                    return false;
                }
            }

            return true;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v3, Property 6: 备份列表完整性 — FileSizeBytes Consistency**
    /// **Validates: Requirements 10.1**
    ///
    /// Each BackupInfo's FileSizeBytes matches the actual file size on disk.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property ListBackups_FileSizeBytes_MatchesActualFileSize()
    {
        return FsCheck.Fluent.Prop.ForAll(BackupListScenarioArb(), config =>
        {
            int matchingCount = config[0];
            int nonMatchingCount = config[1];
            int seed = config[2];
            var rng = new Random(seed);

            if (matchingCount == 0)
                return true; // Nothing to verify for empty case

            var backupDir = CreateTempDir();
            var createdFiles = CreateMatchingBackupFiles(backupDir, matchingCount, rng);
            CreateNonMatchingFiles(backupDir, nonMatchingCount, rng);

            var service = CreateBackupService(backupDir);
            var backups = service.ListBackups();

            // Build a lookup of expected file sizes by file path
            var expectedByPath = createdFiles.ToDictionary(f => f.FilePath, f => f.FileSize);

            foreach (var backup in backups)
            {
                if (!expectedByPath.TryGetValue(backup.FilePath, out var expectedSize))
                    return false; // Unexpected file in results

                if (backup.FileSizeBytes != expectedSize)
                    return false;
            }

            return true;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v3, Property 6: 备份列表完整性 — Sorted Newest First**
    /// **Validates: Requirements 10.1**
    ///
    /// ListBackups returns results sorted by CreatedAt in descending order (newest first).
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property ListBackups_ReturnsSortedNewestFirst()
    {
        return FsCheck.Fluent.Prop.ForAll(BackupListScenarioArb(), config =>
        {
            int matchingCount = config[0];
            int nonMatchingCount = config[1];
            int seed = config[2];
            var rng = new Random(seed);

            if (matchingCount <= 1)
                return true; // Trivially sorted

            var backupDir = CreateTempDir();
            CreateMatchingBackupFiles(backupDir, matchingCount, rng);
            CreateNonMatchingFiles(backupDir, nonMatchingCount, rng);

            var service = CreateBackupService(backupDir);
            var backups = service.ListBackups();

            // Verify descending order
            for (int i = 0; i < backups.Count - 1; i++)
            {
                if (backups[i].CreatedAt < backups[i + 1].CreatedAt)
                    return false;
            }

            return true;
        });
    }
}
