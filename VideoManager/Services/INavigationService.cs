using VideoManager.Models;

namespace VideoManager.Services;

/// <summary>
/// Provides navigation methods for opening windows and dialogs,
/// decoupling view navigation logic from code-behind.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Opens the video player window for the specified video entry.
    /// </summary>
    /// <param name="video">The video entry to play.</param>
    Task OpenVideoPlayerAsync(VideoEntry video);

    /// <summary>
    /// Opens the import dialog and returns the import result.
    /// Returns null if the user cancels the dialog or no videos were imported.
    /// </summary>
    /// <returns>The import result, or null if cancelled or no imports.</returns>
    Task<ImportResult?> OpenImportDialogAsync();

    /// <summary>
    /// Opens the diagnostics window showing performance metrics, memory usage,
    /// cache statistics, and backup management.
    /// </summary>
    Task OpenDiagnosticsAsync();
}
