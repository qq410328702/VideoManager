namespace VideoManager.Services;

/// <summary>
/// Service for caching thumbnail paths in memory to avoid repeated disk access.
/// Provides async loading with cache-first strategy, LRU eviction, and WeakReference support.
/// Non-visible thumbnails are held via WeakReference and may be reclaimed by GC under memory pressure.
/// Visible thumbnails can be pinned with strong references to prevent GC reclamation.
/// </summary>
public interface IThumbnailCacheService
{
    /// <summary>
    /// Asynchronously loads a thumbnail path, returning from cache if available.
    /// On cache miss (including WeakReference target reclaimed by GC), checks if the file
    /// exists on disk, caches the result, and returns.
    /// Returns null if the file does not exist (UI should show a placeholder).
    /// </summary>
    /// <param name="thumbnailPath">The path to the thumbnail file.</param>
    /// <returns>The thumbnail path if the file exists; null otherwise.</returns>
    Task<string?> LoadThumbnailAsync(string thumbnailPath);

    /// <summary>
    /// Clears all cached thumbnail entries, strong references, and resets hit/miss counters.
    /// </summary>
    void ClearCache();

    /// <summary>
    /// Gets the current number of entries in the cache.
    /// </summary>
    int CacheCount { get; }

    /// <summary>
    /// Gets the total number of cache hits since the service was created or last cleared.
    /// </summary>
    int CacheHitCount { get; }

    /// <summary>
    /// Gets the total number of cache misses since the service was created or last cleared.
    /// </summary>
    int CacheMissCount { get; }

    /// <summary>
    /// Pins a thumbnail path with a strong reference to prevent GC from reclaiming it.
    /// Use this for thumbnails currently visible in the UI.
    /// </summary>
    /// <param name="thumbnailPath">The thumbnail path to pin.</param>
    void PinThumbnail(string thumbnailPath);

    /// <summary>
    /// Removes the strong reference for a thumbnail path, allowing GC to reclaim it
    /// if no other references exist.
    /// Use this when a thumbnail is no longer visible in the UI.
    /// </summary>
    /// <param name="thumbnailPath">The thumbnail path to unpin.</param>
    void UnpinThumbnail(string thumbnailPath);

    /// <summary>
    /// Replaces all current strong references with the given set of visible thumbnail paths.
    /// Thumbnails not in the new set will be unpinned.
    /// </summary>
    /// <param name="visiblePaths">The set of currently visible thumbnail paths.</param>
    void UpdateVisibleThumbnails(IEnumerable<string> visiblePaths);
}
