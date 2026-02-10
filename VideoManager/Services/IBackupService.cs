namespace VideoManager.Services;

/// <summary>
/// Record containing information about a database backup file.
/// </summary>
/// <param name="FilePath">Full path to the backup file.</param>
/// <param name="CreatedAt">Date and time when the backup was created.</param>
/// <param name="FileSizeBytes">Size of the backup file in bytes.</param>
public record BackupInfo(string FilePath, DateTime CreatedAt, long FileSizeBytes);

/// <summary>
/// Service for managing SQLite database backups, integrity checks, and restore operations.
/// Provides automatic backup creation via VACUUM INTO, integrity verification via PRAGMA integrity_check,
/// and restore functionality with safety backup of the current database.
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Checks the integrity of the current SQLite database using PRAGMA integrity_check.
    /// </summary>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>True if the database passes integrity check; false if corruption is detected.</returns>
    Task<bool> CheckIntegrityAsync(CancellationToken ct);

    /// <summary>
    /// Creates a backup of the current database using SQLite VACUUM INTO command.
    /// The backup file is named using the pattern: videomanager_backup_{yyyyMMdd_HHmmss}.db
    /// </summary>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>The full file path of the created backup.</returns>
    Task<string> CreateBackupAsync(CancellationToken ct);

    /// <summary>
    /// Restores the database from a backup file. First creates a safety backup of the current
    /// database, then copies the specified backup file over the current database.
    /// </summary>
    /// <param name="backupFilePath">Full path to the backup file to restore from.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    Task RestoreFromBackupAsync(string backupFilePath, CancellationToken ct);

    /// <summary>
    /// Lists all available backup files in the backup directory, sorted by creation time descending.
    /// Only files matching the backup naming pattern are included.
    /// </summary>
    /// <returns>A list of <see cref="BackupInfo"/> records for each backup file found.</returns>
    List<BackupInfo> ListBackups();

    /// <summary>
    /// Removes old backup files, keeping only the most recent N backups as configured
    /// by MaxBackupCount in VideoManagerOptions.
    /// </summary>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    Task CleanupOldBackupsAsync(CancellationToken ct);
}
