using System.Collections.Concurrent;
using System.IO;

namespace VideoManager.Services;

/// <summary>
/// Implementation of <see cref="IThumbnailCacheService"/> that uses a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> for thread-safe in-memory caching
/// of thumbnail paths.
/// </summary>
public class ThumbnailCacheService : IThumbnailCacheService
{
    private readonly ConcurrentDictionary<string, string?> _cache = new();
    private readonly Func<string, bool> _fileExistsCheck;

    /// <summary>
    /// Creates a new ThumbnailCacheService using the default <see cref="File.Exists"/> check.
    /// </summary>
    public ThumbnailCacheService()
        : this(File.Exists)
    {
    }

    /// <summary>
    /// Creates a new ThumbnailCacheService with a custom file existence check.
    /// Used for testing.
    /// </summary>
    /// <param name="fileExistsCheck">A function that checks whether a file exists at the given path.</param>
    internal ThumbnailCacheService(Func<string, bool> fileExistsCheck)
    {
        _fileExistsCheck = fileExistsCheck ?? throw new ArgumentNullException(nameof(fileExistsCheck));
    }

    /// <inheritdoc />
    public Task<string?> LoadThumbnailAsync(string thumbnailPath)
    {
        ArgumentNullException.ThrowIfNull(thumbnailPath);

        // Cache hit → return cached value immediately
        if (_cache.TryGetValue(thumbnailPath, out var cachedValue))
        {
            return Task.FromResult(cachedValue);
        }

        // Cache miss → check file existence, cache result, return
        var result = _fileExistsCheck(thumbnailPath) ? thumbnailPath : null;
        _cache.TryAdd(thumbnailPath, result);

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public void ClearCache()
    {
        _cache.Clear();
    }
}
