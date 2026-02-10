using VideoManager.Services;

namespace VideoManager.Tests.PropertyTests;

/// <summary>
/// Property-based tests for LRU cache invariants.
/// Tests Property 1: LRU 缓存不变量
///
/// **Feature: video-manager-optimization-v3, Property 1: LRU 缓存不变量**
/// **Validates: Requirements 1.1, 1.2, 1.4, 1.5**
///
/// For any sequence of Put and TryGet operations:
/// - The cache count never exceeds the configured capacity
/// - When at capacity, the evicted entry is always the least recently used
/// - After a TryGet hit, that entry is promoted and won't be the next evicted
/// </summary>
public class LruCachePropertyTests
{
    /// <summary>
    /// Generates a random LRU cache test scenario as an int array:
    /// [capacity, numOps, seed]
    /// capacity: 1-20
    /// numOps: 1-200
    /// seed: used to deterministically generate the operation sequence
    /// </summary>
    private static FsCheck.Arbitrary<int[]> CacheScenarioArb()
    {
        var configGen = FsCheck.Fluent.Gen.Select(
            FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, 99999)),
            arr =>
            {
                var capacity = arr.Length > 0 ? (arr[0] % 20) + 1 : 3;     // 1-20
                var numOps = arr.Length > 1 ? (arr[1] % 200) + 1 : 50;     // 1-200
                var seed = arr.Length > 2 ? Math.Abs(arr[2]) + 1 : 1;      // positive seed
                return new int[] { capacity, numOps, seed };
            });

        return FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Where(configGen, c => c.Length == 3));
    }

    /// <summary>
    /// Generates a deterministic sequence of cache operations from a seed.
    /// Returns tuples of (opType, key, value) where opType 0=Put, 1=TryGet.
    /// Keys are bounded to [0, maxKey) to force collisions and evictions.
    /// </summary>
    private static List<(int OpType, int Key, int Value)> GenerateOps(int numOps, int maxKey, int seed)
    {
        var rng = new Random(seed);
        var ops = new List<(int, int, int)>(numOps);
        for (int i = 0; i < numOps; i++)
        {
            var opType = rng.Next(0, 2);       // 0=Put, 1=TryGet
            var key = rng.Next(0, maxKey);
            var value = rng.Next(0, 1000);
            ops.Add((opType, key, value));
        }
        return ops;
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v3, Property 1: LRU 缓存不变量 — Capacity Invariant**
    /// **Validates: Requirements 1.1, 1.2**
    ///
    /// For any sequence of Put/TryGet operations on a cache with a given capacity,
    /// the number of entries in the cache never exceeds the configured capacity.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 200)]
    public FsCheck.Property CacheCount_NeverExceedsCapacity()
    {
        return FsCheck.Fluent.Prop.ForAll(CacheScenarioArb(), config =>
        {
            int capacity = config[0];
            int numOps = config[1];
            int seed = config[2];
            int maxKey = capacity + 10; // more keys than capacity to force evictions

            var cache = new LruCache<int, int>(capacity);
            var ops = GenerateOps(numOps, maxKey, seed);

            foreach (var (opType, key, value) in ops)
            {
                if (opType == 0)
                    cache.Put(key, value);
                else
                    cache.TryGet(key, out _);

                // Invariant: count never exceeds capacity
                if (cache.Count > capacity)
                    return false;
            }

            // Also verify count is non-negative
            return cache.Count >= 0;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v3, Property 1: LRU 缓存不变量 — Eviction Order**
    /// **Validates: Requirements 1.2, 1.5**
    ///
    /// When the cache is at capacity and a new unique key is inserted,
    /// the evicted entry is always the least recently used one.
    /// We track access order with a reference model and verify the cache
    /// evicts the correct entry.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 200)]
    public FsCheck.Property Eviction_AlwaysRemovesLeastRecentlyUsed()
    {
        return FsCheck.Fluent.Prop.ForAll(CacheScenarioArb(), config =>
        {
            int capacity = config[0];
            int numOps = config[1];
            int seed = config[2];
            int maxKey = capacity + 10;

            var cache = new LruCache<int, int>(capacity);
            var ops = GenerateOps(numOps, maxKey, seed);

            // Reference model: ordered list tracking recency (front = most recent)
            var accessOrder = new List<int>();

            foreach (var (opType, key, value) in ops)
            {
                if (opType == 0) // Put
                {
                    bool keyExists = accessOrder.Contains(key);
                    if (!keyExists && accessOrder.Count >= capacity)
                    {
                        // The LRU entry is at the end of our access order list
                        var expectedEvicted = accessOrder[accessOrder.Count - 1];

                        // Perform the Put
                        cache.Put(key, value);

                        // Verify the expected LRU entry was evicted (should not be found)
                        if (cache.TryGet(expectedEvicted, out _))
                            return false;

                        // Update reference model
                        accessOrder.Remove(expectedEvicted);
                        accessOrder.Insert(0, key);
                    }
                    else
                    {
                        cache.Put(key, value);
                        accessOrder.Remove(key);
                        accessOrder.Insert(0, key);
                    }
                }
                else // TryGet
                {
                    var found = cache.TryGet(key, out _);
                    if (found)
                    {
                        accessOrder.Remove(key);
                        accessOrder.Insert(0, key);
                    }
                }
            }

            return true;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v3, Property 1: LRU 缓存不变量 — TryGet Promotion**
    /// **Validates: Requirements 1.4**
    ///
    /// After a TryGet hit on an entry, that entry is promoted to most-recently-used
    /// and will NOT be the next entry evicted. Specifically, after promoting an entry
    /// via TryGet, inserting (capacity - 1) new unique keys should evict all other
    /// original entries but keep the promoted one.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 200)]
    public FsCheck.Property TryGetHit_PromotesEntry_PreventsImmediateEviction()
    {
        var scenarioArb = FsCheck.Fluent.Arb.From(
            FsCheck.Fluent.Gen.Select(
                FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, 99999)),
                arr =>
                {
                    var capacity = arr.Length > 0 ? (arr[0] % 14) + 2 : 3;         // 2-15
                    var accessIndex = arr.Length > 1 ? arr[1] % capacity : 0;       // 0..capacity-1
                    return new int[] { capacity, accessIndex };
                }));

        return FsCheck.Fluent.Prop.ForAll(scenarioArb, config =>
        {
            int capacity = config[0];
            int accessIndex = config[1];

            var cache = new LruCache<int, int>(capacity);

            // Fill the cache to capacity with keys 0..capacity-1
            // After filling: key 0 is LRU, key capacity-1 is MRU
            for (int i = 0; i < capacity; i++)
            {
                cache.Put(i, i * 10);
            }

            // Access the key at accessIndex to promote it to MRU
            var accessKey = accessIndex;
            var found = cache.TryGet(accessKey, out var val);
            if (!found || val != accessKey * 10)
                return false;

            // Insert (capacity - 1) new unique keys to evict all original keys except the promoted one
            var newKeyStart = capacity;
            for (int i = 0; i < capacity - 1; i++)
            {
                cache.Put(newKeyStart + i, (newKeyStart + i) * 10);
            }

            // The promoted key should still be in the cache
            var stillFound = cache.TryGet(accessKey, out var stillVal);
            if (!stillFound || stillVal != accessKey * 10)
                return false;

            // Cache count should be at capacity
            return cache.Count == capacity;
        });
    }

    /// <summary>
    /// **Feature: video-manager-optimization-v3, Property 1: LRU 缓存不变量 — Reference Model Consistency**
    /// **Validates: Requirements 1.1, 1.2, 1.4, 1.5**
    ///
    /// For any sequence of Put/TryGet operations, the LruCache behaves identically
    /// to a simple reference model. After replaying all operations, every key present
    /// in the reference model is also present in the cache with the correct value,
    /// and the cache count matches the model count.
    /// </summary>
    [FsCheck.Xunit.Property(MaxTest = 200)]
    public FsCheck.Property CacheBehavior_MatchesReferenceModel()
    {
        return FsCheck.Fluent.Prop.ForAll(CacheScenarioArb(), config =>
        {
            int capacity = config[0];
            int numOps = config[1];
            int seed = config[2];
            int maxKey = capacity + 10;

            var cache = new LruCache<int, int>(capacity);
            var ops = GenerateOps(numOps, maxKey, seed);

            // Reference model: ordered list of (key, value) pairs
            // Front = most recently used, Back = least recently used
            var model = new List<(int Key, int Value)>();

            foreach (var (opType, key, value) in ops)
            {
                if (opType == 0) // Put
                {
                    cache.Put(key, value);

                    // Reference model Put
                    var existingIdx = model.FindIndex(e => e.Key == key);
                    if (existingIdx >= 0)
                    {
                        model.RemoveAt(existingIdx);
                    }
                    else if (model.Count >= capacity)
                    {
                        model.RemoveAt(model.Count - 1); // evict LRU
                    }
                    model.Insert(0, (key, value));
                }
                else // TryGet
                {
                    var found = cache.TryGet(key, out var cacheVal);
                    var modelIdx = model.FindIndex(e => e.Key == key);
                    var modelFound = modelIdx >= 0;

                    // Cache and model should agree on key existence
                    if (found != modelFound)
                        return false;

                    if (found)
                    {
                        // Values should match
                        if (cacheVal != model[modelIdx].Value)
                            return false;

                        // Promote in reference model
                        var entry = model[modelIdx];
                        model.RemoveAt(modelIdx);
                        model.Insert(0, entry);
                    }
                }
            }

            // Final state: cache and model should have the same count
            if (cache.Count != model.Count)
                return false;

            // Verify all model keys exist in cache with correct values
            foreach (var (key, value) in model)
            {
                if (!cache.TryGet(key, out var cacheVal))
                    return false;
                if (cacheVal != value)
                    return false;
            }

            return true;
        });
    }
}
