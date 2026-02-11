using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for BatchChunkProcessor.
/// Tests Property 4: 批量分块正确性
///
/// **Feature: video-manager-optimization-v4, Property 4: 批量分块正确性**
/// **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.6**
///
/// For any list of length N and any positive integer chunk size C,
/// BatchChunkProcessor.ProcessInChunksAsync should invoke the processing delegate
/// exactly ⌈N/C⌉ times, each chunk size should not exceed C, and all items
/// should be processed exactly once.
/// </summary>
public class BatchChunkProcessorPropertyTests
{
    /// <summary>
    /// Generates a scenario as int[]: [listLength, chunkSize]
    /// listLength: 1-500, chunkSize: 1-100
    /// </summary>
    private static FsCheck.Arbitrary<int[]> ChunkScenarioArb()
    {
        var gen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, 99999)),
            arr =>
            {
                var listLength = arr.Length > 0 ? (arr[0] % 500) + 1 : 10;   // 1-500
                var chunkSize = arr.Length > 1 ? (arr[1] % 100) + 1 : 50;    // 1-100
                return new int[] { listLength, chunkSize };
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(gen, c => c.Length == 2));
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v4, Property 4: 批量分块正确性 — Chunk Count**
    /// **Validates: Requirements 2.1, 2.2, 2.3, 2.6**
    ///
    /// For any list of length N and chunk size C, the processing delegate
    /// is invoked exactly ⌈N/C⌉ times.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 200)]
    public FsCheck.Property ProcessInChunks_InvokesDelegate_CeilingNDivC_Times()
    {
        return FsCheck.Fluent.Prop.ForAll(ChunkScenarioArb(), config =>
        {
            int listLength = config[0];
            int chunkSize = config[1];

            var items = Enumerable.Range(0, listLength).ToList().AsReadOnly();
            int invokeCount = 0;

            var task = BatchChunkProcessor.ProcessInChunksAsync<int>(
                items,
                (chunk, ct) =>
                {
                    Interlocked.Increment(ref invokeCount);
                    return Task.CompletedTask;
                },
                progress: null,
                ct: CancellationToken.None,
                chunkSize: chunkSize);

            task.GetAwaiter().GetResult();

            int expectedChunks = (int)Math.Ceiling((double)listLength / chunkSize);
            return invokeCount == expectedChunks;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v4, Property 4: 批量分块正确性 — Chunk Size Bound**
    /// **Validates: Requirements 2.1, 2.2, 2.3, 2.6**
    ///
    /// For any list of length N and chunk size C, each chunk passed to the delegate
    /// has at most C items.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 200)]
    public FsCheck.Property ProcessInChunks_EachChunkSize_DoesNotExceedC()
    {
        return FsCheck.Fluent.Prop.ForAll(ChunkScenarioArb(), config =>
        {
            int listLength = config[0];
            int chunkSize = config[1];

            var items = Enumerable.Range(0, listLength).ToList().AsReadOnly();
            var chunkSizes = new List<int>();

            var task = BatchChunkProcessor.ProcessInChunksAsync<int>(
                items,
                (chunk, ct) =>
                {
                    lock (chunkSizes)
                    {
                        chunkSizes.Add(chunk.Count);
                    }
                    return Task.CompletedTask;
                },
                progress: null,
                ct: CancellationToken.None,
                chunkSize: chunkSize);

            task.GetAwaiter().GetResult();

            return chunkSizes.All(s => s > 0 && s <= chunkSize);
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v4, Property 4: 批量分块正确性 — All Items Processed Once**
    /// **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.6**
    ///
    /// For any list of length N and chunk size C, all items are processed exactly once.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 200)]
    public FsCheck.Property ProcessInChunks_AllItems_ProcessedExactlyOnce()
    {
        return FsCheck.Fluent.Prop.ForAll(ChunkScenarioArb(), config =>
        {
            int listLength = config[0];
            int chunkSize = config[1];

            var items = Enumerable.Range(0, listLength).ToList().AsReadOnly();
            var processedItems = new List<int>();

            var task = BatchChunkProcessor.ProcessInChunksAsync<int>(
                items,
                (chunk, ct) =>
                {
                    lock (processedItems)
                    {
                        processedItems.AddRange(chunk);
                    }
                    return Task.CompletedTask;
                },
                progress: null,
                ct: CancellationToken.None,
                chunkSize: chunkSize);

            task.GetAwaiter().GetResult();

            // All items processed exactly once: same count and same elements
            if (processedItems.Count != listLength)
                return false;

            processedItems.Sort();
            for (int i = 0; i < listLength; i++)
            {
                if (processedItems[i] != i)
                    return false;
            }

            return true;
        });
    }
}
