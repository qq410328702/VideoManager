using VideoManager.Models;

namespace VideoManager.Services;

/// <summary>
/// Defines the level of a message dialog.
/// </summary>
public enum MessageLevel
{
    Information,
    Warning,
    Error
}

/// <summary>
/// Provides dialog methods for showing edit, delete confirmation, batch tag,
/// batch category, and message dialogs. Decouples dialog creation from
/// code-behind so that ViewModels can trigger dialogs without depending on
/// concrete WPF types.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows the edit dialog for the specified video entry.
    /// </summary>
    /// <param name="video">The video entry to edit.</param>
    /// <returns><c>true</c> if the user saved changes; <c>false</c> otherwise.</returns>
    Task<bool> ShowEditDialogAsync(VideoEntry video);

    /// <summary>
    /// Shows a delete confirmation dialog for a single video.
    /// </summary>
    /// <param name="title">The title of the video to delete.</param>
    /// <returns>
    /// A tuple of (Confirmed, DeleteFile) if the user made a choice, or <c>null</c> if cancelled.
    /// <c>DeleteFile</c> is <c>true</c> to also delete the source file, <c>false</c> to remove from library only.
    /// </returns>
    Task<(bool Confirmed, bool DeleteFile)?> ShowDeleteConfirmAsync(string title);

    /// <summary>
    /// Shows a batch delete confirmation dialog.
    /// </summary>
    /// <param name="count">The number of videos to delete.</param>
    /// <returns>
    /// A tuple of (Confirmed, DeleteFile) if the user made a choice, or <c>null</c> if cancelled.
    /// </returns>
    Task<(bool Confirmed, bool DeleteFile)?> ShowBatchDeleteConfirmAsync(int count);

    /// <summary>
    /// Shows a batch tag selection dialog.
    /// </summary>
    /// <param name="availableTags">The tags available for selection.</param>
    /// <param name="selectedCount">The number of videos that will be affected.</param>
    /// <returns>The list of selected tags, or <c>null</c> if the user cancelled.</returns>
    Task<List<Tag>?> ShowBatchTagDialogAsync(IEnumerable<Tag> availableTags, int selectedCount);

    /// <summary>
    /// Shows a batch category selection dialog.
    /// </summary>
    /// <param name="categories">The categories available for selection.</param>
    /// <param name="selectedCount">The number of videos that will be affected.</param>
    /// <returns>The selected category, or <c>null</c> if the user cancelled.</returns>
    Task<FolderCategory?> ShowBatchCategoryDialogAsync(IEnumerable<FolderCategory> categories, int selectedCount);

    /// <summary>
    /// Shows a message dialog to the user.
    /// </summary>
    /// <param name="message">The message text.</param>
    /// <param name="title">The dialog title.</param>
    /// <param name="level">The message severity level.</param>
    void ShowMessage(string message, string title, MessageLevel level = MessageLevel.Information);

    /// <summary>
    /// Shows a confirmation dialog with OK/Cancel buttons.
    /// </summary>
    /// <param name="message">The confirmation message.</param>
    /// <param name="title">The dialog title.</param>
    /// <returns><c>true</c> if the user confirmed; <c>false</c> otherwise.</returns>
    Task<bool> ShowConfirmAsync(string message, string title);
}
