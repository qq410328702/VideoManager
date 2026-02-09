namespace VideoManager.Services;

/// <summary>
/// Service for caching thumbnail paths in memory to avoid repeated disk access.
/// Provides async loading with cache-first strategy.
/// </summary>
public interface IThumbnailCacheService
{
    /// <summary>
    /// Asynchronously loads a thumbnail path, returning from cache if available.
    /// On cache miss, checks if the file exists on disk, caches the result, and returns.
    /// Returns null if the file does not exist (UI should show a placeholder).
    /// </summary>
    /// <param name="thumbnailPath">The path to the thumbnail file.</param>
    /// <returns>The thumbnail path if the file exists; null otherwise.</returns>
    Task<string?> LoadThumbnailAsync(string thumbnailPath);

    /// <summary>
    /// Clears all cached thumbnail entries.
    /// </summary>
    void ClearCache();
}
