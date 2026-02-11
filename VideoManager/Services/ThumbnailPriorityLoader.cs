using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace VideoManager.Services;

/// <summary>
/// 缩略图加载请求。
/// </summary>
public record ThumbnailRequest(
    int VideoId,
    string ThumbnailPath,
    bool IsVisible,
    CancellationTokenSource Cts);

/// <summary>
/// 基于 Channel&lt;T&gt; 的缩略图优先级加载服务实现。
/// 后台消费者 Task 优先处理可见项，支持 UpdateVisibleItems 取消不可见请求。
/// </summary>
public class ThumbnailPriorityLoader : IThumbnailPriorityLoader
{
    private readonly IThumbnailCacheService _thumbnailCacheService;
    private readonly ILogger<ThumbnailPriorityLoader> _logger;
    private readonly Channel<ThumbnailRequest> _channel;
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _pendingRequests = new();
    private readonly Task _consumerTask;
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _disposed;

    /// <inheritdoc />
    public event Action<int, string?>? ThumbnailLoaded;

    public ThumbnailPriorityLoader(
        IThumbnailCacheService thumbnailCacheService,
        ILogger<ThumbnailPriorityLoader> logger)
    {
        _thumbnailCacheService = thumbnailCacheService ?? throw new ArgumentNullException(nameof(thumbnailCacheService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _channel = Channel.CreateBounded<ThumbnailRequest>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        _consumerTask = Task.Run(() => ConsumeAsync(_disposeCts.Token));
    }

    /// <inheritdoc />
    public void Enqueue(int videoId, string thumbnailPath, bool isVisible)
    {
        if (_disposed) return;

        // Cancel any existing pending request for this videoId
        if (_pendingRequests.TryRemove(videoId, out var existingCts))
        {
            existingCts.Cancel();
            existingCts.Dispose();
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
        var request = new ThumbnailRequest(videoId, thumbnailPath, isVisible, cts);
        _pendingRequests[videoId] = cts;

        if (!_channel.Writer.TryWrite(request))
        {
            _logger.LogWarning("Thumbnail request queue full, dropping request for video {VideoId}", videoId);
            _pendingRequests.TryRemove(videoId, out _);
            cts.Cancel();
            cts.Dispose();
        }
    }

    /// <inheritdoc />
    public void UpdateVisibleItems(IReadOnlySet<int> visibleVideoIds)
    {
        ArgumentNullException.ThrowIfNull(visibleVideoIds);

        foreach (var kvp in _pendingRequests)
        {
            if (!visibleVideoIds.Contains(kvp.Key))
            {
                if (_pendingRequests.TryRemove(kvp.Key, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Background consumer that reads from the channel and processes requests.
    /// Prioritizes visible items by draining the channel and sorting before processing.
    /// </summary>
    private async Task ConsumeAsync(CancellationToken ct)
    {
        var reader = _channel.Reader;

        try
        {
            while (await reader.WaitToReadAsync(ct))
            {
                // Drain all currently available requests
                var batch = new List<ThumbnailRequest>();
                while (reader.TryRead(out var request))
                {
                    batch.Add(request);
                }

                // Sort: visible items first, then non-visible
                batch.Sort((a, b) => b.IsVisible.CompareTo(a.IsVisible));

                foreach (var request in batch)
                {
                    if (ct.IsCancellationRequested) return;

                    // Skip if this request was cancelled (e.g., by UpdateVisibleItems)
                    if (request.Cts.IsCancellationRequested)
                    {
                        continue;
                    }

                    await ProcessRequestAsync(request);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Thumbnail priority loader consumer encountered an unexpected error");
        }
    }

    private async Task ProcessRequestAsync(ThumbnailRequest request)
    {
        try
        {
            if (request.Cts.IsCancellationRequested) return;

            var result = await _thumbnailCacheService.LoadThumbnailAsync(request.ThumbnailPath);

            // Remove from pending after successful load
            _pendingRequests.TryRemove(request.VideoId, out _);

            ThumbnailLoaded?.Invoke(request.VideoId, result);
        }
        catch (OperationCanceledException)
        {
            // Request was cancelled, skip
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load thumbnail for video {VideoId}: {Path}",
                request.VideoId, request.ThumbnailPath);

            // Remove from pending on error too
            _pendingRequests.TryRemove(request.VideoId, out _);

            // Notify with null to indicate failure
            ThumbnailLoaded?.Invoke(request.VideoId, null);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _channel.Writer.TryComplete();
        _disposeCts.Cancel();

        try
        {
            await _consumerTask;
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }

        // Cancel and dispose all pending requests
        foreach (var kvp in _pendingRequests)
        {
            if (_pendingRequests.TryRemove(kvp.Key, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        _disposeCts.Dispose();

        GC.SuppressFinalize(this);
    }
}
