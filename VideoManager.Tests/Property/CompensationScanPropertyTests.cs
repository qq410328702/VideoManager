using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.Property;

/// <summary>
/// Property-based tests for FileWatcherService compensation scan file comparison.
/// **Feature: video-manager-optimization-v4, Property 8: 补偿扫描文件对比正确性**
/// **Validates: Requirements 5.2, 5.3, 5.4**
///
/// For any set of database video records and any file system state, after compensation scan:
/// (a) Records in DB but not on file system should trigger FilesMissing event (newly missing only);
/// (b) Records previously marked as missing (in _knownMissingFiles) whose files have reappeared
///     should trigger FilesRestored event.
/// </summary>
public class CompensationScanPropertyTests : IDisposable
{
    private readonly string _tempDir;

    public CompensationScanPropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CompScanPropTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    /// <summary>
    /// Creates an in-memory SQLite DbContextFactory with the given video entries seeded.
    /// </summary>
    private IDbContextFactory<VideoManagerDbContext> CreateDbFactory(List<VideoEntry> entries)
    {
        var dbName = "CompScanTest_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite($"Data Source={dbName};Mode=Memory;Cache=Shared")
            .Options;

        // Keep a connection open so the in-memory DB persists
        var keepAlive = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbName};Mode=Memory;Cache=Shared");
        keepAlive.Open();

        using (var ctx = new VideoManagerDbContext(options))
        {
            ctx.Database.EnsureCreated();
            if (entries.Count > 0)
            {
                ctx.VideoEntries.AddRange(entries);
                ctx.SaveChanges();
            }
        }

        return new TestDbContextFactory(options, keepAlive);
    }

    /// <summary>
    /// Sets the private _knownMissingFiles field on FileWatcherService via reflection.
    /// </summary>
    private static void SetKnownMissingFiles(FileWatcherService service, HashSet<string> knownMissing)
    {
        var field = typeof(FileWatcherService).GetField(
            "_knownMissingFiles",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field == null)
            throw new InvalidOperationException("Could not find _knownMissingFiles field");

        var existing = (HashSet<string>)field.GetValue(service)!;
        existing.Clear();
        foreach (var path in knownMissing)
            existing.Add(path);
    }

    /// <summary>
    /// Property: For any set of DB video records and any file existence state,
    /// files in DB but NOT on disk that were NOT previously known as missing
    /// should appear in the FilesMissing event.
    ///
    /// **Validates: Requirements 5.2, 5.3**
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property NewlyMissingFiles_TriggerFilesMissingEvent()
    {
        // Generate 1-20 file records
        var fileCountGen = FsCheck.Fluent.Gen.Choose(1, 20);
        // For each file, generate whether it exists on disk (true/false)
        // We use a bitmask approach: generate an int and use bits
        var existenceMaskGen = FsCheck.Fluent.Gen.Choose(0, int.MaxValue);

        return FsCheck.Fluent.Prop.ForAll(
            FsCheck.Fluent.Arb.From(fileCountGen),
            FsCheck.Fluent.Arb.From(existenceMaskGen),
            (fileCount, existenceMask) =>
            {
                // Create file paths and determine existence
                var filePaths = new List<string>();
                var existsOnDisk = new List<bool>();
                for (var i = 0; i < fileCount; i++)
                {
                    var path = Path.Combine(_tempDir, $"video_{i}.mp4");
                    filePaths.Add(path);
                    existsOnDisk.Add((existenceMask & (1 << (i % 31))) != 0);
                }

                // Create actual files for those that "exist"
                foreach (var path in filePaths)
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                for (var i = 0; i < fileCount; i++)
                {
                    if (existsOnDisk[i])
                        File.WriteAllText(filePaths[i], "dummy");
                }

                // Create DB entries (all non-deleted)
                var entries = filePaths.Select((fp, idx) => new VideoEntry
                {
                    Title = $"Video {idx}",
                    FileName = Path.GetFileName(fp),
                    FilePath = fp,
                    ImportedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                }).ToList();

                var factory = CreateDbFactory(entries);
                var service = new FileWatcherService(
                    NullLogger<FileWatcherService>.Instance, factory);

                // Start with empty _knownMissingFiles (first scan scenario)
                var missingPaths = new List<string>();
                service.FilesMissing += (_, args) => missingPaths.AddRange(args.MissingFilePaths);

                service.ExecuteCompensationScanAsync().GetAwaiter().GetResult();

                // Expected: files NOT on disk should be reported as missing
                var expectedMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < fileCount; i++)
                {
                    if (!existsOnDisk[i])
                        expectedMissing.Add(filePaths[i]);
                }

                var actualMissing = new HashSet<string>(missingPaths, StringComparer.OrdinalIgnoreCase);

                // Cleanup temp files
                foreach (var path in filePaths)
                {
                    if (File.Exists(path)) File.Delete(path);
                }

                ((IDisposable)factory).Dispose();

                return actualMissing.SetEquals(expectedMissing);
            });
    }

    /// <summary>
    /// Property: For any set of DB video records where some were previously known as missing
    /// (in _knownMissingFiles) and those files have reappeared on disk,
    /// the FilesRestored event should fire with exactly those restored file paths.
    ///
    /// **Validates: Requirements 5.4**
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property RestoredFiles_TriggerFilesRestoredEvent()
    {
        var fileCountGen = FsCheck.Fluent.Gen.Choose(1, 20);
        // Bitmask for which files were previously known as missing
        var prevMissingMaskGen = FsCheck.Fluent.Gen.Choose(0, int.MaxValue);
        // Bitmask for which files currently exist on disk
        var existenceMaskGen = FsCheck.Fluent.Gen.Choose(0, int.MaxValue);

        return FsCheck.Fluent.Prop.ForAll(
            FsCheck.Fluent.Arb.From(fileCountGen),
            FsCheck.Fluent.Arb.From(prevMissingMaskGen),
            FsCheck.Fluent.Arb.From(existenceMaskGen),
            (fileCount, prevMissingMask, existenceMask) =>
            {
                var filePaths = new List<string>();
                var existsOnDisk = new List<bool>();
                var wasPrevMissing = new List<bool>();

                for (var i = 0; i < fileCount; i++)
                {
                    var path = Path.Combine(_tempDir, $"video_{i}.mp4");
                    filePaths.Add(path);
                    existsOnDisk.Add((existenceMask & (1 << (i % 31))) != 0);
                    wasPrevMissing.Add((prevMissingMask & (1 << (i % 31))) != 0);
                }

                // Create/remove actual files
                foreach (var path in filePaths)
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                for (var i = 0; i < fileCount; i++)
                {
                    if (existsOnDisk[i])
                        File.WriteAllText(filePaths[i], "dummy");
                }

                var entries = filePaths.Select((fp, idx) => new VideoEntry
                {
                    Title = $"Video {idx}",
                    FileName = Path.GetFileName(fp),
                    FilePath = fp,
                    ImportedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                }).ToList();

                var factory = CreateDbFactory(entries);
                var service = new FileWatcherService(
                    NullLogger<FileWatcherService>.Instance, factory);

                // Set up _knownMissingFiles to simulate previous scan state
                var knownMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < fileCount; i++)
                {
                    if (wasPrevMissing[i])
                        knownMissing.Add(filePaths[i]);
                }
                SetKnownMissingFiles(service, knownMissing);

                var restoredPaths = new List<string>();
                service.FilesRestored += (_, args) => restoredPaths.AddRange(args.RestoredFilePaths);

                service.ExecuteCompensationScanAsync().GetAwaiter().GetResult();

                // Expected restored: files that were previously missing AND now exist on disk
                var expectedRestored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < fileCount; i++)
                {
                    if (wasPrevMissing[i] && existsOnDisk[i])
                        expectedRestored.Add(filePaths[i]);
                }

                var actualRestored = new HashSet<string>(restoredPaths, StringComparer.OrdinalIgnoreCase);

                // Cleanup
                foreach (var path in filePaths)
                {
                    if (File.Exists(path)) File.Delete(path);
                }

                ((IDisposable)factory).Dispose();

                return actualRestored.SetEquals(expectedRestored);
            });
    }

    /// <summary>
    /// Property: For any set of DB records and file states, after a compensation scan,
    /// both properties hold simultaneously:
    /// (a) newly missing files (not on disk, not previously known) trigger FilesMissing;
    /// (b) restored files (on disk, previously known missing) trigger FilesRestored.
    /// Files that were already known missing and are still missing should NOT appear in either event.
    /// Files that exist and were never missing should NOT appear in either event.
    ///
    /// **Validates: Requirements 5.2, 5.3, 5.4**
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property CompensationScan_CorrectlyClassifiesAllFileStates()
    {
        var fileCountGen = FsCheck.Fluent.Gen.Choose(1, 20);
        var prevMissingMaskGen = FsCheck.Fluent.Gen.Choose(0, int.MaxValue);
        var existenceMaskGen = FsCheck.Fluent.Gen.Choose(0, int.MaxValue);

        return FsCheck.Fluent.Prop.ForAll(
            FsCheck.Fluent.Arb.From(fileCountGen),
            FsCheck.Fluent.Arb.From(prevMissingMaskGen),
            FsCheck.Fluent.Arb.From(existenceMaskGen),
            (fileCount, prevMissingMask, existenceMask) =>
            {
                var filePaths = new List<string>();
                var existsOnDisk = new List<bool>();
                var wasPrevMissing = new List<bool>();

                for (var i = 0; i < fileCount; i++)
                {
                    var path = Path.Combine(_tempDir, $"video_{i}.mp4");
                    filePaths.Add(path);
                    existsOnDisk.Add((existenceMask & (1 << (i % 31))) != 0);
                    wasPrevMissing.Add((prevMissingMask & (1 << (i % 31))) != 0);
                }

                // Create/remove actual files
                foreach (var path in filePaths)
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                for (var i = 0; i < fileCount; i++)
                {
                    if (existsOnDisk[i])
                        File.WriteAllText(filePaths[i], "dummy");
                }

                var entries = filePaths.Select((fp, idx) => new VideoEntry
                {
                    Title = $"Video {idx}",
                    FileName = Path.GetFileName(fp),
                    FilePath = fp,
                    ImportedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                }).ToList();

                var factory = CreateDbFactory(entries);
                var service = new FileWatcherService(
                    NullLogger<FileWatcherService>.Instance, factory);

                // Set up previous known missing state
                var knownMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < fileCount; i++)
                {
                    if (wasPrevMissing[i])
                        knownMissing.Add(filePaths[i]);
                }
                SetKnownMissingFiles(service, knownMissing);

                var missingPaths = new List<string>();
                var restoredPaths = new List<string>();
                service.FilesMissing += (_, args) => missingPaths.AddRange(args.MissingFilePaths);
                service.FilesRestored += (_, args) => restoredPaths.AddRange(args.RestoredFilePaths);

                service.ExecuteCompensationScanAsync().GetAwaiter().GetResult();

                // Compute expected results
                var expectedMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var expectedRestored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (var i = 0; i < fileCount; i++)
                {
                    var onDisk = existsOnDisk[i];
                    var prevMissing = wasPrevMissing[i];

                    if (!onDisk && !prevMissing)
                    {
                        // Newly missing: not on disk, not previously known
                        expectedMissing.Add(filePaths[i]);
                    }
                    else if (onDisk && prevMissing)
                    {
                        // Restored: on disk, was previously missing
                        expectedRestored.Add(filePaths[i]);
                    }
                    // !onDisk && prevMissing → still missing, no event
                    // onDisk && !prevMissing → normal, no event
                }

                var actualMissing = new HashSet<string>(missingPaths, StringComparer.OrdinalIgnoreCase);
                var actualRestored = new HashSet<string>(restoredPaths, StringComparer.OrdinalIgnoreCase);

                // Cleanup
                foreach (var path in filePaths)
                {
                    if (File.Exists(path)) File.Delete(path);
                }

                ((IDisposable)factory).Dispose();

                return actualMissing.SetEquals(expectedMissing)
                    && actualRestored.SetEquals(expectedRestored);
            });
    }

    /// <summary>
    /// Simple IDbContextFactory wrapper for testing with in-memory SQLite.
    /// Holds a keep-alive connection to prevent the in-memory DB from being destroyed.
    /// </summary>
    private class TestDbContextFactory : IDbContextFactory<VideoManagerDbContext>, IDisposable
    {
        private readonly DbContextOptions<VideoManagerDbContext> _options;
        private readonly Microsoft.Data.Sqlite.SqliteConnection _keepAlive;

        public TestDbContextFactory(
            DbContextOptions<VideoManagerDbContext> options,
            Microsoft.Data.Sqlite.SqliteConnection keepAlive)
        {
            _options = options;
            _keepAlive = keepAlive;
        }

        public VideoManagerDbContext CreateDbContext()
        {
            return new VideoManagerDbContext(_options);
        }

        public void Dispose()
        {
            _keepAlive.Dispose();
        }
    }
}
