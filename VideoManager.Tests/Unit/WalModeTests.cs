using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VideoManager.Data;

namespace VideoManager.Tests.Unit;

/// <summary>
/// Tests that SQLite WAL (Write-Ahead Logging) mode is correctly applied.
/// WAL mode requires a file-based database; it does not work with in-memory databases.
/// </summary>
public class WalModeTests : IDisposable
{
    private readonly string _dbPath;

    public WalModeTests()
    {
        // Create a unique temp file for each test run
        _dbPath = Path.Combine(Path.GetTempPath(), $"wal_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        // Clean up the database file and any WAL/SHM sidecar files
        TryDeleteFile(_dbPath);
        TryDeleteFile(_dbPath + "-wal");
        TryDeleteFile(_dbPath + "-shm");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    private VideoManagerDbContext CreateFileBasedContext()
    {
        var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite($"Data Source={_dbPath};Cache=Shared")
            .Options;
        var context = new VideoManagerDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    /// Verifies that after executing PRAGMA journal_mode=WAL on a file-based SQLite database,
    /// querying PRAGMA journal_mode returns "wal".
    /// This mirrors the WAL configuration in App.xaml.cs OnStartup.
    /// </summary>
    [Fact]
    public void PragmaJournalMode_AfterSettingWal_ReturnsWal()
    {
        // Arrange
        using var context = CreateFileBasedContext();

        // Act – apply WAL mode just like App.xaml.cs does at startup
        context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

        // Assert – query the current journal mode
        var connection = context.Database.GetDbConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        var journalMode = command.ExecuteScalar()?.ToString();

        Assert.Equal("wal", journalMode);
    }

    /// <summary>
    /// Verifies that the full set of performance PRAGMAs from App.xaml.cs can be applied
    /// and that WAL mode persists after setting synchronous and cache_size.
    /// </summary>
    [Fact]
    public void PragmaJournalMode_AfterAllPerformancePragmas_ReturnsWal()
    {
        // Arrange
        using var context = CreateFileBasedContext();

        // Act – apply all three PRAGMAs as done in App.xaml.cs OnStartup
        context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        context.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");
        context.Database.ExecuteSqlRaw("PRAGMA cache_size=-8000;");

        // Assert – WAL mode should still be active
        var connection = context.Database.GetDbConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        var journalMode = command.ExecuteScalar()?.ToString();

        Assert.Equal("wal", journalMode);
    }
}
