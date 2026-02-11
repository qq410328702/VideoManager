using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using VideoManager.Services;

namespace VideoManager.Tests.Property;

/// <summary>
/// Property-based tests for ThumbnailPriorityLoader priority ordering.
/// **Feature: video-manager-optimization-v4, Property 1: 缩略图优先级排序**
/// **Validates: Requirements 1.1, 1.3**
///
/// For any set of thumbnail load requests with mixed visibility,
/// all requests marked as visible (IsVisible=true) should be consumed/processed
/// before all non-visible requests.
/// </summary>
public class ThumbnailPriorityPropertyTests
{
    /// <summary>
    /// A stub IThumbnailCacheService that records the order of LoadThumbnailAsync calls.
    /// Uses a TaskCompletionSource to block the first call (signaling that the consumer
    /// has drained a batch), and then records all subsequent calls in order.
    /// </summary>
    private class OrderRecordingCacheService : IThumbnailCacheService
    {
        private readonly List<string> _loadOrder = new();
        private readonly object _lock = new();
        private readonly SemaphoreSlim _completionSignal;
        private readonly int _expectedCount;
        private int _callCount;

        /// <summary>
        /// TCS that blocks the first LoadThumbnailAsync call.
        /// When the first call arrives, we know the consumer has drained a batch.
        /// We signal FirstCallReached, then block until UnblockFirstCall is called.
        /// </summary>
        private readonly TaskCompletionSource<bool> _firstCallTcs = new();
        private readonly SemaphoreSlim _firstCallReached = new(0, 1);
        private bool _isFirstCall = true;

        public OrderRecordingCacheService(int expectedCount)
        {
            _expectedCount = expectedCount;
            _completionSignal = new SemaphoreSlim(0, 1);
        }

        public IReadOnlyList<string> LoadOrder
        {
            get { lock (_lock) { return _loadOrder.ToList(); } }
        }

        public SemaphoreSlim CompletionSignal => _completionSignal;
        public SemaphoreSlim FirstCallReached => _firstCallReached;

        /// <summary>
        /// Unblocks the first LoadThumbnailAsync call so processing can proceed.
        /// </summary>
        public void UnblockFirstCall() => _firstCallTcs.TrySetResult(true);

        public async Task<string?> LoadThumbnailAsync(string thumbnailPath)
        {
            bool shouldBlock;
            lock (_lock)
            {
                shouldBlock = _isFirstCall;
                _isFirstCall = false;
            }

            if (shouldBlock)
            {
                // Signal that the consumer has drained a batch and started processing
                _firstCallReached.Release();
                // Block asynchronously until unblocked — this yields the consumer thread
                // so remaining items can be enqueued and will be in the next batch drain
                await _firstCallTcs.Task;
            }

            lock (_lock)
            {
                _loadOrder.Add(thumbnailPath);
                _callCount++;
                if (_callCount >= _expectedCount)
                {
                    _completionSignal.Release();
                }
            }
            return thumbnailPath;
        }

        public void ClearCache() { }
        public int CacheCount => 0;
        public int CacheHitCount => 0;
        public int CacheMissCount => 0;
        public void PinThumbnail(string thumbnailPath) { }
        public void UnpinThumbnail(string thumbnailPath) { }
        public void UpdateVisibleThumbnails(IEnumerable<string> visiblePaths) { }
    }

    /// <summary>
    /// Property: For any set of mixed-visibility thumbnail requests enqueued together,
    /// all visible requests are processed before all non-visible requests.
    ///
    /// Strategy:
    /// 1. Create the loader and enqueue ONE non-visible request immediately
    /// 2. Wait for the consumer to pick it up (FirstCallReached signals)
    /// 3. While the consumer is blocked on the first item's LoadThumbnailAsync,
    ///    enqueue all remaining items (they accumulate in the channel)
    /// 4. Unblock the first call — the consumer finishes the first item,
    ///    then drains all remaining items into a single batch and sorts them
    /// 5. Verify: the first item (non-visible) is processed first (it was already
    ///    in progress), then all visible items, then remaining non-visible items
    ///
    /// **Validates: Requirements 1.1, 1.3**
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property VisibleRequests_AreProcessedBefore_NonVisibleRequests()
    {
        // Generate between 1 and 10 visible requests and 1 and 10 non-visible requests
        var visibleCountGen = FsCheck.Fluent.Gen.Choose(1, 10);
        var nonVisibleCountGen = FsCheck.Fluent.Gen.Choose(1, 10);

        return FsCheck.Fluent.Prop.ForAll(
            FsCheck.Fluent.Arb.From(visibleCountGen),
            FsCheck.Fluent.Arb.From(nonVisibleCountGen),
            (visibleCount, nonVisibleCount) =>
            {
                var totalCount = visibleCount + nonVisibleCount;
                var cacheService = new OrderRecordingCacheService(totalCount);
                var loader = new ThumbnailPriorityLoader(
                    cacheService,
                    NullLogger<ThumbnailPriorityLoader>.Instance);

                try
                {
                    // Step 1: Enqueue one non-visible request to trigger the consumer
                    loader.Enqueue(1, "NV_first", isVisible: false);

                    // Step 2: Wait for the consumer to pick it up and block on LoadThumbnailAsync
                    var firstReached = cacheService.FirstCallReached.Wait(TimeSpan.FromSeconds(5));
                    if (!firstReached) return false;

                    // Step 3: Now the consumer is blocked. Enqueue all remaining items.
                    // These will accumulate in the channel and be drained as a single batch
                    // after the first item completes.
                    var videoId = 2;

                    // Enqueue remaining non-visible requests
                    for (var i = 1; i < nonVisibleCount; i++)
                    {
                        loader.Enqueue(videoId++, $"NV_{i}", isVisible: false);
                    }

                    // Enqueue all visible requests
                    for (var i = 0; i < visibleCount; i++)
                    {
                        loader.Enqueue(videoId++, $"V_{i}", isVisible: true);
                    }

                    // Step 4: Unblock the first call — consumer finishes it,
                    // then drains remaining items into a sorted batch
                    cacheService.UnblockFirstCall();

                    // Wait for all requests to be processed (timeout 5s)
                    var completed = cacheService.CompletionSignal.Wait(TimeSpan.FromSeconds(5));
                    if (!completed) return false;

                    var loadOrder = cacheService.LoadOrder;
                    if (loadOrder.Count != totalCount) return false;

                    // Step 5: Verify ordering
                    // First item is "NV_first" (it was already being processed)
                    // After that, all visible ("V_") should come before non-visible ("NV_")
                    var remainingOrder = loadOrder.Skip(1).ToList();

                    var seenNonVisible = false;
                    foreach (var path in remainingOrder)
                    {
                        if (path.StartsWith("NV_"))
                        {
                            seenNonVisible = true;
                        }
                        else if (path.StartsWith("V_"))
                        {
                            if (seenNonVisible) return false;
                        }
                    }

                    return true;
                }
                finally
                {
                    cacheService.UnblockFirstCall(); // Ensure cleanup even on failure
                    loader.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            });
    }
}

/// <summary>
/// Property-based tests for ThumbnailPriorityLoader scroll cancellation behavior.
/// **Feature: video-manager-optimization-v4, Property 2: 滚动时取消不可见请求**
/// **Validates: Requirements 1.2**
///
/// For any set of pending thumbnail load requests and any new visible set,
/// after calling UpdateVisibleItems, all pending requests NOT in the new visible set
/// should have their CancellationTokenSource cancelled, while requests IN the visible
/// set should remain uncancelled.
/// </summary>
public class ThumbnailPriorityScrollCancellationPropertyTests
{
    /// <summary>
    /// A cache service where the first LoadThumbnailAsync call blocks on a
    /// TaskCompletionSource (async, non-thread-blocking). Subsequent calls
    /// complete immediately and record which paths were processed.
    /// This lets us call UpdateVisibleItems while the consumer is awaiting
    /// the first item, ensuring remaining batch items have their CTS checked.
    /// </summary>
    private class FirstCallBlockingCacheService : IThumbnailCacheService
    {
        private readonly TaskCompletionSource<string?> _firstCallTcs = new();
        private readonly List<string> _processedPaths = new();
        private readonly object _lock = new();
        private readonly SemaphoreSlim _firstCallReached = new(0, 1);
        private readonly SemaphoreSlim _completionSignal;
        private readonly int _expectedCount;
        private int _callCount;
        private bool _isFirstCall = true;

        public FirstCallBlockingCacheService(int expectedCount)
        {
            _expectedCount = Math.Max(expectedCount, 1);
            _completionSignal = new SemaphoreSlim(0, 1);
        }

        public IReadOnlyList<string> ProcessedPaths
        {
            get { lock (_lock) { return _processedPaths.ToList(); } }
        }

        public SemaphoreSlim CompletionSignal => _completionSignal;
        public SemaphoreSlim FirstCallReached => _firstCallReached;

        /// <summary>
        /// Unblocks the first LoadThumbnailAsync call.
        /// </summary>
        public void UnblockFirstCall(string? result = "done")
        {
            _firstCallTcs.TrySetResult(result);
        }

        public async Task<string?> LoadThumbnailAsync(string thumbnailPath)
        {
            bool shouldBlock;
            lock (_lock)
            {
                shouldBlock = _isFirstCall;
                _isFirstCall = false;
            }

            if (shouldBlock)
            {
                // Signal that the first call has been reached
                _firstCallReached.Release();
                // Block asynchronously until unblocked
                await _firstCallTcs.Task;
            }

            lock (_lock)
            {
                _processedPaths.Add(thumbnailPath);
                _callCount++;
                if (_callCount >= _expectedCount)
                {
                    _completionSignal.Release();
                }
            }
            return thumbnailPath;
        }

        public void ClearCache() { }
        public int CacheCount => 0;
        public int CacheHitCount => 0;
        public int CacheMissCount => 0;
        public void PinThumbnail(string thumbnailPath) { }
        public void UnpinThumbnail(string thumbnailPath) { }
        public void UpdateVisibleThumbnails(IEnumerable<string> visiblePaths) { }
    }

    /// <summary>
    /// Property: For any set of pending requests and any new visible set,
    /// after UpdateVisibleItems, only requests in the visible set are processed;
    /// requests not in the visible set are cancelled and skipped.
    ///
    /// Strategy:
    /// 1. Generate random pending video IDs and a random subset as the new visible set
    /// 2. Enqueue all requests; the consumer drains them into a batch
    /// 3. The first item blocks on an async TCS — consumer yields, doesn't block thread
    /// 4. Call UpdateVisibleItems with the visible subset — cancels non-visible CTS
    /// 5. Unblock the first item — consumer resumes, checks CTS on remaining items
    /// 6. Verify: non-visible items are skipped, visible items are processed
    ///
    /// Note: The first item in the batch is always processed (it was already past the
    /// CTS check when UpdateVisibleItems was called). We account for this in verification.
    ///
    /// **Validates: Requirements 1.2**
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property ScrollCancellation_CancelsNonVisiblePendingRequests()
    {
        // Generate 2-12 total request count (need at least 2 for meaningful test)
        var totalCountGen = FsCheck.Fluent.Gen.Choose(2, 12);
        // Seed for determining the visible subset
        var seedGen = FsCheck.Fluent.Gen.Choose(0, 10000);

        return FsCheck.Fluent.Prop.ForAll(
            FsCheck.Fluent.Arb.From(totalCountGen),
            FsCheck.Fluent.Arb.From(seedGen),
            (totalCount, visibleSeed) =>
            {
                // Create unique video IDs: 1..totalCount
                var allVideoIds = Enumerable.Range(1, totalCount).ToList();

                // Use the seed to pick a random visible subset (at least 1 visible)
                var rng = new Random(visibleSeed);
                var visibleCount = rng.Next(1, totalCount + 1);
                var shuffled = allVideoIds.OrderBy(_ => rng.Next()).ToList();
                var visibleIds = new HashSet<int>(shuffled.Take(visibleCount));
                var nonVisibleIds = allVideoIds.Where(id => !visibleIds.Contains(id)).ToHashSet();

                // The first item in the batch will always be processed (already past CTS check).
                // Remaining visible items will also be processed.
                // We expect: visibleCount items processed (+ possibly 1 non-visible if it was first).
                // The first enqueued item (videoId=1) will be first in the batch since all have
                // same IsVisible=true, so batch order matches enqueue order.
                var firstItemId = 1;
                var firstItemIsVisible = visibleIds.Contains(firstItemId);

                // Expected processed count: all visible items + first item if it's non-visible
                var expectedProcessedCount = visibleIds.Count + (firstItemIsVisible ? 0 : 1);

                var cacheService = new FirstCallBlockingCacheService(expectedProcessedCount);
                var loader = new ThumbnailPriorityLoader(
                    cacheService,
                    NullLogger<ThumbnailPriorityLoader>.Instance);

                try
                {
                    // Let the consumer task start
                    Thread.Sleep(30);

                    // Enqueue all requests as visible so they enter _pendingRequests
                    foreach (var videoId in allVideoIds)
                    {
                        loader.Enqueue(videoId, $"path_{videoId}", isVisible: true);
                    }

                    // Wait for the consumer to pick up the first item and block on it
                    var firstReached = cacheService.FirstCallReached.Wait(TimeSpan.FromSeconds(5));
                    if (!firstReached) return false;

                    // Now the consumer is awaiting the first item's TCS.
                    // All other items are in the batch, waiting to be processed.
                    // Their CTS entries are in _pendingRequests.

                    // Simulate scroll: update visible items to the new subset
                    loader.UpdateVisibleItems((IReadOnlySet<int>)visibleIds);

                    // Unblock the first item so the consumer can proceed
                    cacheService.UnblockFirstCall();

                    // Wait for expected items to be processed
                    var completed = cacheService.CompletionSignal.Wait(TimeSpan.FromSeconds(5));

                    // Small extra window for any unexpected processing
                    Thread.Sleep(100);

                    var processed = cacheService.ProcessedPaths;
                    var processedIds = processed
                        .Select(p => int.Parse(p.Replace("path_", "")))
                        .ToHashSet();

                    // Verify: every visible ID should be processed
                    var allVisibleProcessed = visibleIds.All(id => processedIds.Contains(id));

                    // Verify: no non-visible ID should be processed
                    // (except the first item if it was non-visible — it was already in LoadThumbnailAsync)
                    var unexpectedNonVisible = nonVisibleIds
                        .Where(id => id != firstItemId)
                        .Any(id => processedIds.Contains(id));

                    return allVisibleProcessed && !unexpectedNonVisible;
                }
                finally
                {
                    cacheService.UnblockFirstCall();
                    loader.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            });
    }
}

/// <summary>
/// Property-based tests for ThumbnailPriorityLoader error recovery behavior.
/// **Feature: video-manager-optimization-v4, Property 3: 缩略图加载错误恢复**
/// **Validates: Requirements 1.5**
///
/// For any sequence of thumbnail load requests where some cause loading exceptions,
/// the loader should continue processing all subsequent non-failing requests
/// without stopping the entire consumer loop due to a single failure.
/// </summary>
public class ThumbnailPriorityErrorRecoveryPropertyTests
{
    /// <summary>
    /// A stub IThumbnailCacheService that throws for paths in the failing set
    /// and succeeds for all others, recording which paths were successfully processed.
    /// Uses a gate to batch all requests together before processing starts.
    /// </summary>
    private class ErrorRecoveryCacheService : IThumbnailCacheService
    {
        private readonly HashSet<string> _failingPaths;
        private readonly List<string> _successPaths = new();
        private readonly List<string> _failedPaths = new();
        private readonly object _lock = new();
        private readonly SemaphoreSlim _completionSignal;
        private readonly int _expectedCallCount;
        private int _callCount;
        private readonly ManualResetEventSlim _gate = new(false);

        public ErrorRecoveryCacheService(HashSet<string> failingPaths, int expectedCallCount)
        {
            _failingPaths = failingPaths;
            _expectedCallCount = expectedCallCount;
            _completionSignal = new SemaphoreSlim(0, 1);
        }

        public IReadOnlyList<string> SuccessPaths
        {
            get { lock (_lock) { return _successPaths.ToList(); } }
        }

        public IReadOnlyList<string> FailedPaths
        {
            get { lock (_lock) { return _failedPaths.ToList(); } }
        }

        public SemaphoreSlim CompletionSignal => _completionSignal;

        public void OpenGate() => _gate.Set();

        public Task<string?> LoadThumbnailAsync(string thumbnailPath)
        {
            _gate.Wait(TimeSpan.FromSeconds(5));

            lock (_lock)
            {
                _callCount++;

                if (_failingPaths.Contains(thumbnailPath))
                {
                    _failedPaths.Add(thumbnailPath);
                    // Signal completion if this was the last expected call
                    if (_callCount >= _expectedCallCount)
                        _completionSignal.Release();
                    throw new IOException($"Simulated load failure for {thumbnailPath}");
                }

                _successPaths.Add(thumbnailPath);
                if (_callCount >= _expectedCallCount)
                    _completionSignal.Release();
            }

            return Task.FromResult<string?>(thumbnailPath);
        }

        public void ClearCache() { }
        public int CacheCount => 0;
        public int CacheHitCount => 0;
        public int CacheMissCount => 0;
        public void PinThumbnail(string thumbnailPath) { }
        public void UnpinThumbnail(string thumbnailPath) { }
        public void UpdateVisibleThumbnails(IEnumerable<string> visiblePaths) { }
    }

    /// <summary>
    /// Property: For any sequence of thumbnail load requests where a subset causes
    /// exceptions, the loader continues processing all remaining non-failing requests.
    /// The consumer loop does not stop due to individual request failures.
    ///
    /// Strategy:
    /// 1. Generate a random total count and a random subset of indices that will fail
    /// 2. Enqueue all requests; use a gate to batch them together
    /// 3. Open the gate and wait for all calls to complete
    /// 4. Verify: every non-failing path was successfully processed
    /// 5. Verify: the consumer processed all requests (didn't stop early)
    ///
    /// **Validates: Requirements 1.5**
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 100)]
    public FsCheck.Property ErrorRecovery_ContinuesProcessing_AfterIndividualFailures()
    {
        // Generate between 2 and 15 total requests
        var totalCountGen = FsCheck.Fluent.Gen.Choose(2, 15);
        // Seed for determining which requests fail
        var seedGen = FsCheck.Fluent.Gen.Choose(0, 10000);

        return FsCheck.Fluent.Prop.ForAll(
            FsCheck.Fluent.Arb.From(totalCountGen),
            FsCheck.Fluent.Arb.From(seedGen),
            (totalCount, failSeed) =>
            {
                // Use the seed to pick a random subset of failing indices (at least 1 failing, at least 1 succeeding)
                var rng = new Random(failSeed);
                var failCount = rng.Next(1, totalCount); // 1..totalCount-1 failures
                var allIndices = Enumerable.Range(0, totalCount).ToList();
                var shuffled = allIndices.OrderBy(_ => rng.Next()).ToList();
                var failingIndices = new HashSet<int>(shuffled.Take(failCount));

                var failingPaths = new HashSet<string>();
                var expectedSuccessPaths = new HashSet<string>();

                for (var i = 0; i < totalCount; i++)
                {
                    var path = $"thumb_{i}";
                    if (failingIndices.Contains(i))
                        failingPaths.Add(path);
                    else
                        expectedSuccessPaths.Add(path);
                }

                // The cache service will be called for every request (totalCount times)
                var cacheService = new ErrorRecoveryCacheService(failingPaths, totalCount);
                var loader = new ThumbnailPriorityLoader(
                    cacheService,
                    NullLogger<ThumbnailPriorityLoader>.Instance);

                try
                {
                    // Let the consumer task start
                    Thread.Sleep(50);

                    // Enqueue all requests as visible
                    for (var i = 0; i < totalCount; i++)
                    {
                        loader.Enqueue(i + 1, $"thumb_{i}", isVisible: true);
                    }

                    // Let the consumer drain all items into a batch
                    Thread.Sleep(50);

                    // Open the gate so processing begins
                    cacheService.OpenGate();

                    // Wait for all requests to be processed (both success and failure)
                    var completed = cacheService.CompletionSignal.Wait(TimeSpan.FromSeconds(5));
                    if (!completed) return false;

                    // Small extra window for any late processing
                    Thread.Sleep(50);

                    var successPaths = new HashSet<string>(cacheService.SuccessPaths);
                    var failedPaths = new HashSet<string>(cacheService.FailedPaths);

                    // Verify: all expected success paths were processed
                    var allSuccessProcessed = expectedSuccessPaths.All(p => successPaths.Contains(p));

                    // Verify: all failing paths were attempted (consumer didn't skip them)
                    var allFailuresAttempted = failingPaths.All(p => failedPaths.Contains(p));

                    // Verify: total calls = totalCount (consumer didn't stop early)
                    var totalProcessed = successPaths.Count + failedPaths.Count;
                    var allProcessed = totalProcessed == totalCount;

                    return allSuccessProcessed && allFailuresAttempted && allProcessed;
                }
                finally
                {
                    loader.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            });
    }
}

