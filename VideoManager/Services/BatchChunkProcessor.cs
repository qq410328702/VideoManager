namespace VideoManager.Services;

/// <summary>
/// 批量操作分块处理器。将大批量操作拆分为固定大小的块，
/// 每块之间通过 Task.Yield() 让出 UI 线程。
/// </summary>
public static class BatchChunkProcessor
{
    /// <summary>
    /// 默认块大小。
    /// </summary>
    public const int DefaultChunkSize = 50;

    /// <summary>
    /// 分块执行异步操作。
    /// </summary>
    /// <typeparam name="T">操作项类型</typeparam>
    /// <param name="items">待处理项集合</param>
    /// <param name="processChunk">处理单个块的异步委托</param>
    /// <param name="progress">进度报告</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="chunkSize">块大小，默认 50</param>
    public static async Task ProcessInChunksAsync<T>(
        IReadOnlyList<T> items,
        Func<IReadOnlyList<T>, CancellationToken, Task> processChunk,
        IProgress<(int completed, int total)>? progress,
        CancellationToken ct,
        int chunkSize = DefaultChunkSize)
    {
        if (items.Count == 0)
            return;

        if (chunkSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");

        var total = items.Count;
        var completed = 0;

        for (int offset = 0; offset < total; offset += chunkSize)
        {
            ct.ThrowIfCancellationRequested();

            var count = Math.Min(chunkSize, total - offset);
            var chunk = items.Skip(offset).Take(count).ToList().AsReadOnly();

            await processChunk(chunk, ct);

            completed += count;
            progress?.Report((completed, total));

            await Task.Yield();
        }
    }
}
