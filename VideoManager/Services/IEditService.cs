using VideoManager.Models;

namespace VideoManager.Services;

/// <summary>
/// Service for editing video metadata (title, description, tag associations).
/// </summary>
public interface IEditService
{
    /// <summary>
    /// Updates the title and description of a video entry.
    /// </summary>
    /// <param name="videoId">The ID of the video to update.</param>
    /// <param name="title">The new title. Must not be null, empty, or whitespace.</param>
    /// <param name="description">The new description (nullable).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated video entry.</returns>
    /// <exception cref="ArgumentException">Thrown when title is null, empty, or whitespace.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when the video is not found.</exception>
    Task<VideoEntry> UpdateVideoInfoAsync(int videoId, string title, string? description, CancellationToken ct);

    /// <summary>
    /// Adds a tag association to a video entry.
    /// </summary>
    /// <param name="videoId">The ID of the video.</param>
    /// <param name="tagId">The ID of the tag to add.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Thrown when the video or tag is not found.</exception>
    Task AddTagToVideoAsync(int videoId, int tagId, CancellationToken ct);

    /// <summary>
    /// Removes a tag association from a video entry.
    /// </summary>
    /// <param name="videoId">The ID of the video.</param>
    /// <param name="tagId">The ID of the tag to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Thrown when the video or tag is not found.</exception>
    Task RemoveTagFromVideoAsync(int videoId, int tagId, CancellationToken ct);

    /// <summary>
    /// Adds a tag to multiple videos in a batch operation.
    /// Videos that already have the tag will be skipped.
    /// </summary>
    /// <param name="videoIds">The IDs of the videos to tag.</param>
    /// <param name="tagId">The ID of the tag to add.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentException">Thrown when videoIds is null or empty.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when the tag is not found.</exception>
    Task BatchAddTagAsync(List<int> videoIds, int tagId, CancellationToken ct);

    /// <summary>
    /// Moves multiple videos to a category in a batch operation.
    /// Videos that are already in the category will be skipped.
    /// </summary>
    /// <param name="videoIds">The IDs of the videos to move.</param>
    /// <param name="categoryId">The ID of the target category.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentException">Thrown when videoIds is null or empty.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when the category is not found.</exception>
    Task BatchMoveToCategoryAsync(List<int> videoIds, int categoryId, CancellationToken ct);

    /// <summary>
    /// Updates the color of a tag.
    /// </summary>
    /// <param name="tagId">The ID of the tag to update.</param>
    /// <param name="color">The hex color string (e.g. "#FF5722"), or null to reset to default.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Thrown when the tag is not found.</exception>
    Task UpdateTagColorAsync(int tagId, string? color, CancellationToken ct);

    /// <summary>
    /// Adds a category association to a video entry.
    /// </summary>
    Task AddCategoryToVideoAsync(int videoId, int categoryId, CancellationToken ct);

    /// <summary>
    /// Removes a category association from a video entry.
    /// </summary>
    Task RemoveCategoryFromVideoAsync(int videoId, int categoryId, CancellationToken ct);

}
