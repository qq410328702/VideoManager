using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<ThumbnailCacheService> _logger;

    /// <summary>
    /// Creates a new ThumbnailCacheService using the default <see cref="File.Exists"/> check.
    /// </summary>
    public ThumbnailCacheService(ILogger<ThumbnailCacheService> logger)
        : this(File.Exists, logger)
    {
    }

    /// <summary>
    /// Creates a new ThumbnailCacheService with a custom file existence check.
    /// Used for testing.
    /// </summary>
    /// <param name="fileExistsCheck">A function that checks whether a file exists at the given path.</param>
    /// <param name="logger">The logger instance.</param>
    internal ThumbnailCacheService(Func<string, bool> fileExistsCheck, ILogger<ThumbnailCacheService> logger)
    {
        _fileExistsCheck = fileExistsCheck ?? throw new ArgumentNullException(nameof(fileExistsCheck));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<string?> LoadThumbnailAsync(string thumbnailPath)
    {
        ArgumentNullException.ThrowIfNull(thumbnailPath);

        // Cache hit → return cached value immediately
        if (_cache.TryGetValue(thumbnailPath, out var cachedValue))
        {
            _logger.LogDebug("Thumbnail cache hit: {ThumbnailPath}", thumbnailPath);
            return Task.FromResult(cachedValue);
        }

        // Cache miss → check file existence, cache result, return
        _logger.LogDebug("Thumbnail cache miss: {ThumbnailPath}", thumbnailPath);
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
