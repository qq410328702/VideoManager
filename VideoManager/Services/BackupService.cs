using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VideoManager.Data;
using VideoManager.Models;

namespace VideoManager.Services;

/// <summary>
/// Implementation of <see cref="IBackupService"/> that manages SQLite database backups.
/// Uses VACUUM INTO for atomic backup creation, PRAGMA integrity_check for verification,
/// and a safe restore flow that backs up the current database before overwriting.
/// </summary>
public class BackupService : IBackupService
{
    /// <summary>
    /// Prefix used for backup file names.
    /// </summary>
    internal const string BackupFilePrefix = "videomanager_backup_";

    /// <summary>
    /// Date/time format used in backup file names.
    /// </summary>
    internal const string BackupTimestampFormat = "yyyyMMdd_HHmmss";

    /// <summary>
    /// File extension for backup files.
    /// </summary>
    internal const string BackupFileExtension = ".db";

    private readonly ILogger<BackupService> _logger;
    private readonly IOptions<VideoManagerOptions> _options;
    private readonly IDbContextFactory<VideoManagerDbContext> _dbContextFactory;
    private readonly string _databasePath;
    private readonly string _backupDirectory;

    /// <summary>
    /// Creates a new BackupService instance.
    /// </summary>
    /// <param name="logger">Logger for structured logging output.</param>
    /// <param name="options">Application configuration options containing backup settings.</param>
    /// <param name="dbContextFactory">Factory for creating database context instances.</param>
    public BackupService(
        ILogger<BackupService> logger,
        IOptions<VideoManagerOptions> options,
        IDbContextFactory<VideoManagerDbContext> dbContextFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));

        // Resolve database path from the DbContext configuration
        _databasePath = ResolveDatabasePath();
        _backupDirectory = ResolveBackupDirectory();

        Directory.CreateDirectory(_backupDirectory);

        _logger.LogInformation(
            "BackupService initialized. Database: {DatabasePath}, BackupDir: {BackupDir}, MaxBackups: {MaxBackups}",
            _databasePath, _backupDirectory, _options.Value.MaxBackupCount);
    }

    /// <inheritdoc />
    public async Task<bool> CheckIntegrityAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Starting database integrity check");

            await using var context = await _dbContextFactory.CreateDbContextAsync(ct);
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync(ct);

            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";

            await using var reader = await command.ExecuteReaderAsync(ct);
            var results = new List<string>();

            while (await reader.ReadAsync(ct))
            {
                results.Add(reader.GetString(0));
            }

            var isOk = results.Count == 1 && string.Equals(results[0], "ok", StringComparison.OrdinalIgnoreCase);

            if (isOk)
            {
                _logger.LogInformation("Database integrity check passed");
            }
            else
            {
                _logger.LogError(
                    "Database integrity check FAILED. Results: {IntegrityResults}",
                    string.Join("; ", results));
            }

            return isOk;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Database integrity check was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during database integrity check");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<string> CreateBackupAsync(CancellationToken ct)
    {
        var timestamp = DateTime.Now.ToString(BackupTimestampFormat, CultureInfo.InvariantCulture);
        var backupFileName = $"{BackupFilePrefix}{timestamp}{BackupFileExtension}";
        var backupFilePath = Path.Combine(_backupDirectory, backupFileName);

        try
        {
            _logger.LogInformation("Creating database backup: {BackupPath}", backupFilePath);

            await using var context = await _dbContextFactory.CreateDbContextAsync(ct);
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync(ct);

            // Use VACUUM INTO for atomic backup (doesn't block reads)
            await using var command = connection.CreateCommand();
            command.CommandText = $"VACUUM INTO @backupPath;";

            var param = command.CreateParameter();
            param.ParameterName = "@backupPath";
            param.Value = backupFilePath;
            command.Parameters.Add(param);

            await command.ExecuteNonQueryAsync(ct);

            var fileInfo = new FileInfo(backupFilePath);
            _logger.LogInformation(
                "Database backup created successfully: {BackupPath} ({SizeBytes} bytes)",
                backupFilePath, fileInfo.Length);

            return backupFilePath;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Database backup creation was cancelled");
            // Clean up partial backup file if it exists
            TryDeleteFile(backupFilePath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create database backup: {BackupPath}", backupFilePath);
            // Clean up partial backup file if it exists
            TryDeleteFile(backupFilePath);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RestoreFromBackupAsync(string backupFilePath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(backupFilePath);

        if (!File.Exists(backupFilePath))
        {
            throw new FileNotFoundException("Backup file not found", backupFilePath);
        }

        _logger.LogInformation("Starting database restore from: {BackupPath}", backupFilePath);

        // Step 1: Create a safety backup of the current database
        string safetyBackupPath;
        try
        {
            safetyBackupPath = await CreateBackupAsync(ct);
            _logger.LogInformation("Safety backup created before restore: {SafetyBackupPath}", safetyBackupPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create safety backup before restore. Aborting restore operation.");
            throw new InvalidOperationException("Cannot restore: failed to create safety backup of current database.", ex);
        }

        // Step 2: Copy the backup file over the current database
        try
        {
            // Close all connections by disposing the context factory's connections
            // We use SqliteConnection directly to ensure all connections are closed
            SqliteConnection.ClearAllPools();

            // Wait briefly for connections to fully close
            await Task.Delay(100, ct);

            File.Copy(backupFilePath, _databasePath, overwrite: true);

            _logger.LogInformation(
                "Database restored successfully from: {BackupPath}. Safety backup at: {SafetyBackupPath}",
                backupFilePath, safetyBackupPath);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Database restore was cancelled after safety backup was created: {SafetyBackupPath}", safetyBackupPath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to restore database from {BackupPath}. Current database may be intact. Safety backup at: {SafetyBackupPath}",
                backupFilePath, safetyBackupPath);
            throw;
        }
    }

    /// <inheritdoc />
    public List<BackupInfo> ListBackups()
    {
        var backups = new List<BackupInfo>();

        if (!Directory.Exists(_backupDirectory))
        {
            _logger.LogDebug("Backup directory does not exist: {BackupDir}", _backupDirectory);
            return backups;
        }

        var backupFiles = Directory.GetFiles(_backupDirectory, $"{BackupFilePrefix}*{BackupFileExtension}");

        foreach (var filePath in backupFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var createdAt = ParseBackupTimestamp(fileName);

            if (createdAt.HasValue)
            {
                var fileInfo = new FileInfo(filePath);
                backups.Add(new BackupInfo(filePath, createdAt.Value, fileInfo.Length));
            }
            else
            {
                _logger.LogDebug("Skipping file with unrecognized backup name format: {FilePath}", filePath);
            }
        }

        // Sort by creation time descending (newest first)
        backups.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));

        _logger.LogDebug("Found {BackupCount} backup files in {BackupDir}", backups.Count, _backupDirectory);
        return backups;
    }

    /// <inheritdoc />
    public Task CleanupOldBackupsAsync(CancellationToken ct)
    {
        var maxBackupCount = _options.Value.MaxBackupCount;
        var backups = ListBackups();

        if (backups.Count <= maxBackupCount)
        {
            _logger.LogDebug(
                "No backup cleanup needed. Current: {CurrentCount}, Max: {MaxCount}",
                backups.Count, maxBackupCount);
            return Task.CompletedTask;
        }

        // Backups are already sorted newest-first; remove the oldest ones
        var backupsToDelete = backups.Skip(maxBackupCount).ToList();

        _logger.LogInformation(
            "Cleaning up {DeleteCount} old backups (keeping {KeepCount} of {TotalCount})",
            backupsToDelete.Count, maxBackupCount, backups.Count);

        foreach (var backup in backupsToDelete)
        {
            ct.ThrowIfCancellationRequested();
            TryDeleteFile(backup.FilePath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves the database file path from the DbContext configuration.
    /// Falls back to the default application data directory path.
    /// </summary>
    private string ResolveDatabasePath()
    {
        try
        {
            using var context = _dbContextFactory.CreateDbContext();
            var connectionString = context.Database.GetConnectionString();
            if (!string.IsNullOrEmpty(connectionString))
            {
                var builder = new SqliteConnectionStringBuilder(connectionString);
                if (!string.IsNullOrEmpty(builder.DataSource))
                {
                    return Path.GetFullPath(builder.DataSource);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve database path from DbContext, using default");
        }

        // Fallback to default path
        var appDir = AppContext.BaseDirectory;
        return Path.Combine(appDir, "Data", "videomanager.db");
    }

    /// <summary>
    /// Resolves the backup directory from configuration or uses the default.
    /// </summary>
    private string ResolveBackupDirectory()
    {
        var configuredDir = _options.Value.BackupDirectory;
        if (!string.IsNullOrWhiteSpace(configuredDir))
        {
            return configuredDir;
        }

        // Default: AppDir/Backups
        return Path.Combine(AppContext.BaseDirectory, "Backups");
    }

    /// <summary>
    /// Parses the timestamp from a backup file name.
    /// Expected format: videomanager_backup_yyyyMMdd_HHmmss
    /// </summary>
    /// <param name="fileNameWithoutExtension">The file name without extension.</param>
    /// <returns>The parsed DateTime, or null if the format doesn't match.</returns>
    internal static DateTime? ParseBackupTimestamp(string fileNameWithoutExtension)
    {
        if (string.IsNullOrEmpty(fileNameWithoutExtension) ||
            !fileNameWithoutExtension.StartsWith(BackupFilePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var timestampPart = fileNameWithoutExtension[BackupFilePrefix.Length..];

        if (DateTime.TryParseExact(
                timestampPart,
                BackupTimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var result))
        {
            return result;
        }

        return null;
    }

    /// <summary>
    /// Attempts to delete a file, logging any errors without throwing.
    /// </summary>
    private void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogDebug("Deleted file: {FilePath}", filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete file: {FilePath}", filePath);
        }
    }
}
