using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VideoManager.Models;

namespace VideoManager.Services;

/// <summary>
/// Implementation of <see cref="IThumbnailCacheService"/> that uses an
/// <see cref="LruCache{TKey,TValue}"/> with <see cref="WeakReference{T}"/> values
/// for thread-safe in-memory caching of thumbnail paths with LRU eviction.
/// 
/// Non-visible thumbnails are wrapped in WeakReference, allowing GC to reclaim them
/// under memory pressure. Currently visible thumbnails are kept alive via strong
/// references in <see cref="_pinnedThumbnails"/>.
/// </summary>
public class ThumbnailCacheService : IThumbnailCacheService
{
    private readonly LruCache<string, WeakReference<string>> _cache;
    private readonly HashSet<string> _pinnedThumbnails;
    private readonly object _pinLock = new();
    private readonly Func<string, bool> _fileExistsCheck;
    private readonly ILogger<ThumbnailCacheService> _logger;
    private int _cacheHitCount;
    private int _cacheMissCount;

    /// <summary>
    /// Creates a new ThumbnailCacheService using the default <see cref="File.Exists"/> check
    /// and reading cache capacity from <see cref="VideoManagerOptions.ThumbnailCacheMaxSize"/>.
    /// </summary>
    public ThumbnailCacheService(IOptions<VideoManagerOptions> options, ILogger<ThumbnailCacheService> logger)
        : this(File.Exists, options, logger)
    {
    }

    /// <summary>
    /// Creates a new ThumbnailCacheService with a custom file existence check.
    /// Used for testing.
    /// </summary>
    /// <param name="fileExistsCheck">A function that checks whether a file exists at the given path.</param>
    /// <param name="options">The video manager options containing cache configuration.</param>
    /// <param name="logger">The logger instance.</param>
    internal ThumbnailCacheService(Func<string, bool> fileExistsCheck, IOptions<VideoManagerOptions> options, ILogger<ThumbnailCacheService> logger)
    {
        _fileExistsCheck = fileExistsCheck ?? throw new ArgumentNullException(nameof(fileExistsCheck));
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var maxSize = options.Value.ThumbnailCacheMaxSize;
        if (maxSize <= 0)
        {
            maxSize = 1000;
        }

        _cache = new LruCache<string, WeakReference<string>>(maxSize);
        _pinnedThumbnails = new HashSet<string>();
        _logger.LogInformation("ThumbnailCacheService initialized with LRU cache capacity: {Capacity}", maxSize);
    }

    /// <inheritdoc />
    public int CacheCount => _cache.Count;

    /// <inheritdoc />
    public int CacheHitCount => Volatile.Read(ref _cacheHitCount);

    /// <inheritdoc />
    public int CacheMissCount => Volatile.Read(ref _cacheMissCount);

    /// <inheritdoc />
    public Task<string?> LoadThumbnailAsync(string thumbnailPath)
    {
        ArgumentNullException.ThrowIfNull(thumbnailPath);

        // Try to get from cache
        if (_cache.TryGet(thumbnailPath, out var weakRef) && weakRef != null)
        {
            // WeakReference found in cache — check if target is still alive
            if (weakRef.TryGetTarget(out var cachedValue))
            {
                Interlocked.Increment(ref _cacheHitCount);
                _logger.LogDebug("Thumbnail cache hit: {ThumbnailPath}", thumbnailPath);
                // If the cached value is the null sentinel, return null (file didn't exist)
                return Task.FromResult<string?>(IsNullSentinel(cachedValue) ? null : cachedValue);
            }

            // WeakReference target was reclaimed by GC — treat as cache miss and reload
            _logger.LogDebug("Thumbnail WeakReference reclaimed, reloading: {ThumbnailPath}", thumbnailPath);
        }

        // Cache miss → check file existence, cache result, return
        Interlocked.Increment(ref _cacheMissCount);
        _logger.LogDebug("Thumbnail cache miss: {ThumbnailPath}", thumbnailPath);

        string? result = _fileExistsCheck(thumbnailPath) ? thumbnailPath : null;

        if (result != null)
        {
            _cache.Put(thumbnailPath, new WeakReference<string>(result));
        }
        else
        {
            // For non-existent files, we still cache a WeakReference to an empty sentinel
            // so we don't re-check the file system on every call.
            // We use a special approach: store a WeakReference with a known sentinel value.
            _cache.Put(thumbnailPath, new WeakReference<string>(NullSentinel));
        }

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public void ClearCache()
    {
        _cache.Clear();
        lock (_pinLock)
        {
            _pinnedThumbnails.Clear();
        }
        Interlocked.Exchange(ref _cacheHitCount, 0);
        Interlocked.Exchange(ref _cacheMissCount, 0);
    }

    /// <inheritdoc />
    public void PinThumbnail(string thumbnailPath)
    {
        ArgumentNullException.ThrowIfNull(thumbnailPath);
        lock (_pinLock)
        {
            _pinnedThumbnails.Add(thumbnailPath);
        }
    }

    /// <inheritdoc />
    public void UnpinThumbnail(string thumbnailPath)
    {
        ArgumentNullException.ThrowIfNull(thumbnailPath);
        lock (_pinLock)
        {
            _pinnedThumbnails.Remove(thumbnailPath);
        }
    }

    /// <inheritdoc />
    public void UpdateVisibleThumbnails(IEnumerable<string> visiblePaths)
    {
        ArgumentNullException.ThrowIfNull(visiblePaths);
        lock (_pinLock)
        {
            _pinnedThumbnails.Clear();
            foreach (var path in visiblePaths)
            {
                _pinnedThumbnails.Add(path);
            }
        }
    }

    /// <summary>
    /// A sentinel string used to represent a cached null (file-not-found) result.
    /// Since WeakReference&lt;string&gt; cannot hold null, we use this interned sentinel
    /// to distinguish "file doesn't exist" from "WeakReference was reclaimed".
    /// Being interned, it will never be garbage collected.
    /// </summary>
    private static readonly string NullSentinel = string.Intern("__NULL_THUMBNAIL_SENTINEL__");

    /// <summary>
    /// Checks whether a cached value is the null sentinel.
    /// </summary>
    private static bool IsNullSentinel(string value) => ReferenceEquals(value, NullSentinel);
}
