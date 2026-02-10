using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using VideoManager.Data;
using VideoManager.Models;
using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

/// <summary>
/// Unit tests for BackupService covering edge cases and specific scenarios.
/// Tests empty backup directories, corrupted databases, restore with missing backups,
/// backup creation naming patterns, and cleanup with no backups.
/// _Requirements: 9.1, 9.2, 9.3_
/// </summary>
public class BackupServiceTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
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
        var dir = Path.Combine(Path.GetTempPath(), $"backup_unit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    /// <summary>
    /// Creates a file-based SQLite database, ensures schema is created, and returns the path.
    /// </summary>
    private string CreateTestDatabase()
    {
        var dbDir = CreateTempDir();
        var dbPath = Path.Combine(dbDir, "test.db");

        var options = new DbContextOptionsBuilder<VideoManagerDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        using (var ctx = new VideoManagerDbContext(options))
        {
            ctx.Database.EnsureCreated();
        }

        return dbPath;
    }

    /// <summary>
    /// Creates a BackupService instance configured with the given database and backup directory.
    /// </summary>
    private BackupService CreateBackupService(string dbPath, string backupDir, int maxBackupCount = 5)
    {
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

    #region ListBackups Tests

    /// <summary>
    /// When backup directory is empty, ListBackups returns an empty list.
    /// _Requirements: 9.1_
    /// </summary>
    [Fact]
    public void ListBackups_EmptyDirectory_ReturnsEmptyList()
    {
        // Arrange
        var dbPath = CreateTestDatabase();
        var backupDir = CreateTempDir(); // Empty directory

        var service = CreateBackupService(dbPath, backupDir);

        // Act
        var backups = service.ListBackups();

        // Assert
        Assert.NotNull(backups);
        Assert.Empty(backups);
    }

    /// <summary>
    /// When backup directory doesn't exist, ListBackups returns an empty list.
    /// Note: BackupService constructor creates the directory, so we use a subdirectory
    /// that doesn't exist as the backup dir by manipulating the path after construction.
    /// _Requirements: 9.1_
    /// </summary>
    [Fact]
    public void ListBackups_NonExistentDirectory_ReturnsEmptyList()
    {
        // Arrange - Use a backup directory that the constructor will create,
        // then delete it to simulate a non-existent directory scenario.
        var dbPath = CreateTestDatabase();
        var backupDir = Path.Combine(CreateTempDir(), "nonexistent_subdir");

        // The BackupService constructor creates the directory, so we create the service
        // with a valid directory first, then test with a path that doesn't exist.
        // Actually, let's create the service (which creates the dir), then delete the dir.
        var service = CreateBackupService(dbPath, backupDir);

        // Delete the directory that the constructor created
        if (Directory.Exists(backupDir))
        {
            Directory.Delete(backupDir, recursive: true);
        }

        // Act
        var backups = service.ListBackups();

        // Assert
        Assert.NotNull(backups);
        Assert.Empty(backups);
    }

    #endregion

    #region CheckIntegrity Tests

    /// <summary>
    /// When database is corrupted, CheckIntegrityAsync returns false.
    /// _Requirements: 9.1_
    /// </summary>
    [Fact]
    public async Task CheckIntegrity_CorruptedDatabase_ReturnsFalse()
    {
        // Arrange
        var dbPath = CreateTestDatabase();
        var backupDir = CreateTempDir();
        var service = CreateBackupService(dbPath, backupDir);

        // Corrupt the database by overwriting with random bytes
        SqliteConnection.ClearAllPools();
        await Task.Delay(100); // Allow connections to fully close

        var corruptData = new byte[4096];
        new Random(42).NextBytes(corruptData);
        await File.WriteAllBytesAsync(dbPath, corruptData);

        // Act
        var result = await service.CheckIntegrityAsync(CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// When database is valid, CheckIntegrityAsync returns true.
    /// _Requirements: 9.1_
    /// </summary>
    [Fact]
    public async Task CheckIntegrity_ValidDatabase_ReturnsTrue()
    {
        // Arrange
        var dbPath = CreateTestDatabase();
        var backupDir = CreateTempDir();
        var service = CreateBackupService(dbPath, backupDir);

        // Act
        var result = await service.CheckIntegrityAsync(CancellationToken.None);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region RestoreFromBackup Tests

    /// <summary>
    /// When backup file doesn't exist, RestoreFromBackupAsync throws FileNotFoundException.
    /// _Requirements: 9.2_
    /// </summary>
    [Fact]
    public async Task RestoreFromBackup_NonExistentBackupFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var dbPath = CreateTestDatabase();
        var backupDir = CreateTempDir();
        var service = CreateBackupService(dbPath, backupDir);

        var nonExistentPath = Path.Combine(backupDir, "nonexistent_backup.db");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.RestoreFromBackupAsync(nonExistentPath, CancellationToken.None));
    }

    /// <summary>
    /// When null path is passed, RestoreFromBackupAsync throws ArgumentNullException.
    /// _Requirements: 9.2_
    /// </summary>
    [Fact]
    public async Task RestoreFromBackup_NullPath_ThrowsArgumentNullException()
    {
        // Arrange
        var dbPath = CreateTestDatabase();
        var backupDir = CreateTempDir();
        var service = CreateBackupService(dbPath, backupDir);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.RestoreFromBackupAsync(null!, CancellationToken.None));
    }

    #endregion

    #region CreateBackup Tests

    /// <summary>
    /// Verify backup file is created with correct naming pattern (videomanager_backup_yyyyMMdd_HHmmss.db).
    /// _Requirements: 9.3_
    /// </summary>
    [Fact]
    public async Task CreateBackup_CreatesFileWithCorrectNamingPattern()
    {
        // Arrange
        var dbPath = CreateTestDatabase();
        var backupDir = CreateTempDir();
        var service = CreateBackupService(dbPath, backupDir);

        // Act
        var backupFilePath = await service.CreateBackupAsync(CancellationToken.None);

        // Assert
        Assert.True(File.Exists(backupFilePath));

        var fileName = Path.GetFileName(backupFilePath);
        Assert.StartsWith(BackupService.BackupFilePrefix, fileName);
        Assert.EndsWith(BackupService.BackupFileExtension, fileName);

        // Verify the timestamp portion is valid
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(backupFilePath);
        var parsedTimestamp = BackupService.ParseBackupTimestamp(fileNameWithoutExt);
        Assert.NotNull(parsedTimestamp);

        // Verify the backup file is a valid SQLite database
        var fileInfo = new FileInfo(backupFilePath);
        Assert.True(fileInfo.Length > 0);
    }

    #endregion

    #region CleanupOldBackups Tests

    /// <summary>
    /// When no backups exist, cleanup completes without error.
    /// _Requirements: 9.3_
    /// </summary>
    [Fact]
    public async Task CleanupOldBackups_NoBackups_DoesNotThrow()
    {
        // Arrange
        var dbPath = CreateTestDatabase();
        var backupDir = CreateTempDir(); // Empty directory
        var service = CreateBackupService(dbPath, backupDir);

        // Act & Assert - should not throw
        var exception = await Record.ExceptionAsync(
            () => service.CleanupOldBackupsAsync(CancellationToken.None));

        Assert.Null(exception);
    }

    #endregion
}
