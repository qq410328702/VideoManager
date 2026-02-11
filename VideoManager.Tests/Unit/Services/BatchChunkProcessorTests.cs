using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="BatchChunkProcessor"/>.
/// Tests edge cases: empty list, chunk size larger than list, progress reporting, etc.
/// </summary>
public class BatchChunkProcessorTests
{
    [Fact]
    public async Task ProcessInChunksAsync_EmptyList_DoesNotInvokeDelegate()
    {
        var items = new List<int>().AsReadOnly();
        int invokeCount = 0;

        await BatchChunkProcessor.ProcessInChunksAsync<int>(
            items,
            (chunk, ct) => { invokeCount++; return Task.CompletedTask; },
            progress: null,
            ct: CancellationToken.None);

        Assert.Equal(0, invokeCount);
    }

    [Fact]
    public async Task ProcessInChunksAsync_ChunkSizeLargerThanList_SingleChunk()
    {
        var items = Enumerable.Range(0, 10).ToList().AsReadOnly();
        var chunks = new List<IReadOnlyList<int>>();

        await BatchChunkProcessor.ProcessInChunksAsync<int>(
            items,
            (chunk, ct) => { chunks.Add(chunk); return Task.CompletedTask; },
            progress: null,
            ct: CancellationToken.None,
            chunkSize: 100);

        Assert.Single(chunks);
        Assert.Equal(10, chunks[0].Count);
    }

    [Fact]
    public async Task ProcessInChunksAsync_ReportsProgress()
    {
        var items = Enumerable.Range(0, 120).ToList().AsReadOnly();
        var progressReports = new List<(int completed, int total)>();
        // Use a synchronous IProgress implementation to avoid async callback timing issues
        var progress = new SynchronousProgress<(int completed, int total)>(p => progressReports.Add(p));

        await BatchChunkProcessor.ProcessInChunksAsync<int>(
            items,
            (chunk, ct) => Task.CompletedTask,
            progress: progress,
            ct: CancellationToken.None,
            chunkSize: 50);

        // Should have 3 progress reports: 50/120, 100/120, 120/120
        Assert.Equal(3, progressReports.Count);
        Assert.Equal((50, 120), progressReports[0]);
        Assert.Equal((100, 120), progressReports[1]);
        Assert.Equal((120, 120), progressReports[2]);
    }

    /// <summary>
    /// Synchronous IProgress implementation that invokes the callback immediately
    /// on the calling thread, avoiding async dispatch issues with Progress&lt;T&gt;.
    /// </summary>
    private class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public SynchronousProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }

    [Fact]
    public async Task ProcessInChunksAsync_DefaultChunkSize_Is50()
    {
        Assert.Equal(50, BatchChunkProcessor.DefaultChunkSize);

        var items = Enumerable.Range(0, 100).ToList().AsReadOnly();
        int invokeCount = 0;

        await BatchChunkProcessor.ProcessInChunksAsync<int>(
            items,
            (chunk, ct) => { invokeCount++; return Task.CompletedTask; },
            progress: null,
            ct: CancellationToken.None);

        Assert.Equal(2, invokeCount);
    }

    [Fact]
    public async Task ProcessInChunksAsync_SingleItem_SingleChunk()
    {
        var items = new List<int> { 42 }.AsReadOnly();
        var processedItems = new List<int>();

        await BatchChunkProcessor.ProcessInChunksAsync<int>(
            items,
            (chunk, ct) => { processedItems.AddRange(chunk); return Task.CompletedTask; },
            progress: null,
            ct: CancellationToken.None);

        Assert.Single(processedItems);
        Assert.Equal(42, processedItems[0]);
    }

    [Fact]
    public void ProcessInChunksAsync_ZeroChunkSize_ThrowsArgumentOutOfRangeException()
    {
        var items = Enumerable.Range(0, 10).ToList().AsReadOnly();

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            BatchChunkProcessor.ProcessInChunksAsync<int>(
                items,
                (chunk, ct) => Task.CompletedTask,
                progress: null,
                ct: CancellationToken.None,
                chunkSize: 0));
    }

    [Fact]
    public async Task ProcessInChunksAsync_AlreadyCancelledToken_ThrowsImmediately()
    {
        var items = Enumerable.Range(0, 10).ToList().AsReadOnly();
        var cts = new CancellationTokenSource();
        cts.Cancel();
        int invokeCount = 0;

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            BatchChunkProcessor.ProcessInChunksAsync<int>(
                items,
                (chunk, ct) => { invokeCount++; return Task.CompletedTask; },
                progress: null,
                ct: cts.Token));

        Assert.Equal(0, invokeCount);
    }
}
