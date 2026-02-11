using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for BatchChunkProcessor cancellation behavior.
/// Tests Property 5: 批量分块取消
///
/// **Feature: video-manager-optimization-v4, Property 5: 批量分块取消**
/// **Validates: Requirements 2.5**
///
/// For any list of length N and any chunk size C, when the cancellation token
/// is triggered after the K-th chunk completes (1 ≤ K < ⌈N/C⌉), exactly K chunks
/// should be processed and remaining chunks should not be executed.
/// </summary>
public class BatchChunkCancellationPropertyTests
{
    /// <summary>
    /// Generates a cancellation scenario as int[]: [listLength, chunkSize, cancelAfterChunk]
    /// Ensures cancelAfterChunk is at least 1 and less than total chunks.
    /// </summary>
    private static FsCheck.Arbitrary<int[]> CancellationScenarioArb()
    {
        var gen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, 99999)),
            arr =>
            {
                var chunkSize = arr.Length > 0 ? (arr[0] % 50) + 1 : 10;       // 1-50
                // Need at least 2 chunks to test cancellation between them
                var minLength = chunkSize + 1;
                var listLength = arr.Length > 1 ? minLength + (arr[1] % 500) : minLength + 50; // enough for 2+ chunks
                var totalChunks = (int)Math.Ceiling((double)listLength / chunkSize);
                var cancelAfterChunk = arr.Length > 2 ? (arr[2] % (totalChunks - 1)) + 1 : 1; // 1..totalChunks-1
                return new int[] { listLength, chunkSize, cancelAfterChunk };
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(gen, c =>
                c.Length == 3 &&
                c[0] > 0 && c[1] > 0 && c[2] >= 1 &&
                c[2] < (int)Math.Ceiling((double)c[0] / c[1])));
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v4, Property 5: 批量分块取消 — Cancellation Stops Processing**
    /// **Validates: Requirements 2.5**
    ///
    /// When cancellation is triggered after the K-th chunk, exactly K chunks are processed.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 200)]
    public FsCheck.Property CancelAfterKChunks_ExactlyKChunksProcessed()
    {
        return FsCheck.Fluent.Prop.ForAll(CancellationScenarioArb(), config =>
        {
            int listLength = config[0];
            int chunkSize = config[1];
            int cancelAfterChunk = config[2];

            var items = Enumerable.Range(0, listLength).ToList().AsReadOnly();
            var cts = new CancellationTokenSource();
            int chunksProcessed = 0;

            try
            {
                var task = BatchChunkProcessor.ProcessInChunksAsync<int>(
                    items,
                    (chunk, ct) =>
                    {
                        chunksProcessed++;
                        if (chunksProcessed == cancelAfterChunk)
                        {
                            cts.Cancel();
                        }
                        return Task.CompletedTask;
                    },
                    progress: null,
                    ct: cts.Token,
                    chunkSize: chunkSize);

                task.GetAwaiter().GetResult();

                // If we get here without exception, something is wrong
                // (cancellation should have thrown)
                return false;
            }
            catch (OperationCanceledException)
            {
                // Expected: cancellation was triggered
                return chunksProcessed == cancelAfterChunk;
            }
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v4, Property 5: 批量分块取消 — Remaining Chunks Not Executed**
    /// **Validates: Requirements 2.5**
    ///
    /// When cancellation is triggered after K chunks, the total items processed
    /// equals exactly K * chunkSize (or less for the last chunk if K is the last partial chunk).
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 200)]
    public FsCheck.Property CancelAfterKChunks_RemainingItemsNotProcessed()
    {
        return FsCheck.Fluent.Prop.ForAll(CancellationScenarioArb(), config =>
        {
            int listLength = config[0];
            int chunkSize = config[1];
            int cancelAfterChunk = config[2];

            var items = Enumerable.Range(0, listLength).ToList().AsReadOnly();
            var cts = new CancellationTokenSource();
            var processedItems = new List<int>();
            int chunksProcessed = 0;

            try
            {
                var task = BatchChunkProcessor.ProcessInChunksAsync<int>(
                    items,
                    (chunk, ct) =>
                    {
                        chunksProcessed++;
                        processedItems.AddRange(chunk);
                        if (chunksProcessed == cancelAfterChunk)
                        {
                            cts.Cancel();
                        }
                        return Task.CompletedTask;
                    },
                    progress: null,
                    ct: cts.Token,
                    chunkSize: chunkSize);

                task.GetAwaiter().GetResult();
                return false;
            }
            catch (OperationCanceledException)
            {
                // Exactly cancelAfterChunk * chunkSize items should be processed
                // (unless the last processed chunk was partial)
                int expectedItems = Math.Min(cancelAfterChunk * chunkSize, listLength);
                return processedItems.Count == expectedItems;
            }
        });
    }
}
