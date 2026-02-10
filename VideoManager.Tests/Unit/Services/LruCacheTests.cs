using VideoManager.Services;

namespace VideoManager.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="LruCache{TKey, TValue}"/>.
/// Tests specific examples and edge cases for the LRU cache implementation.
/// </summary>
public class LruCacheTests
{
    [Fact]
    public void Constructor_WithZeroCapacity_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<string, int>(0));
    }

    [Fact]
    public void Constructor_WithNegativeCapacity_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<string, int>(-1));
    }

    [Fact]
    public void Constructor_SetsCapacity()
    {
        var cache = new LruCache<string, int>(10);
        Assert.Equal(10, cache.Capacity);
    }

    [Fact]
    public void NewCache_HasZeroCount()
    {
        var cache = new LruCache<string, int>(5);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Put_SingleItem_CountIsOne()
    {
        var cache = new LruCache<string, int>(5);
        cache.Put("a", 1);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void TryGet_ExistingKey_ReturnsTrueAndValue()
    {
        var cache = new LruCache<string, int>(5);
        cache.Put("key", 42);

        var found = cache.TryGet("key", out var value);

        Assert.True(found);
        Assert.Equal(42, value);
    }

    [Fact]
    public void TryGet_NonExistingKey_ReturnsFalse()
    {
        var cache = new LruCache<string, int>(5);

        var found = cache.TryGet("missing", out var value);

        Assert.False(found);
        Assert.Equal(default, value);
    }

    [Fact]
    public void TryGet_EmptyCache_ReturnsFalse()
    {
        var cache = new LruCache<string, string>(5);

        var found = cache.TryGet("any", out var value);

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void Put_DuplicateKey_UpdatesValue()
    {
        var cache = new LruCache<string, int>(5);
        cache.Put("key", 1);
        cache.Put("key", 2);

        cache.TryGet("key", out var value);

        Assert.Equal(2, value);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Put_ExceedsCapacity_EvictsLeastRecentlyUsed()
    {
        var cache = new LruCache<string, int>(2);
        cache.Put("a", 1);
        cache.Put("b", 2);
        cache.Put("c", 3); // Should evict "a"

        Assert.Equal(2, cache.Count);
        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void TryGet_PromotesEntry_PreventsEviction()
    {
        var cache = new LruCache<string, int>(2);
        cache.Put("a", 1);
        cache.Put("b", 2);

        // Access "a" to promote it
        cache.TryGet("a", out _);

        // Insert "c" — should evict "b" (now least recently used), not "a"
        cache.Put("c", 3);

        Assert.True(cache.TryGet("a", out var aVal));
        Assert.Equal(1, aVal);
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void Put_UpdateExistingKey_PromotesEntry()
    {
        var cache = new LruCache<string, int>(2);
        cache.Put("a", 1);
        cache.Put("b", 2);

        // Update "a" to promote it
        cache.Put("a", 10);

        // Insert "c" — should evict "b" (now least recently used), not "a"
        cache.Put("c", 3);

        Assert.True(cache.TryGet("a", out var aVal));
        Assert.Equal(10, aVal);
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void CapacityOne_EvictsOnEveryNewInsert()
    {
        var cache = new LruCache<string, int>(1);

        cache.Put("a", 1);
        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet("a", out _));

        cache.Put("b", 2);
        Assert.Equal(1, cache.Count);
        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _));
    }

    [Fact]
    public void Remove_ExistingKey_ReturnsTrueAndRemovesEntry()
    {
        var cache = new LruCache<string, int>(5);
        cache.Put("a", 1);

        var removed = cache.Remove("a");

        Assert.True(removed);
        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet("a", out _));
    }

    [Fact]
    public void Remove_NonExistingKey_ReturnsFalse()
    {
        var cache = new LruCache<string, int>(5);

        var removed = cache.Remove("missing");

        Assert.False(removed);
    }

    [Fact]
    public void Remove_FreesSlotForNewEntry()
    {
        var cache = new LruCache<string, int>(2);
        cache.Put("a", 1);
        cache.Put("b", 2);

        cache.Remove("a");
        cache.Put("c", 3);

        Assert.Equal(2, cache.Count);
        Assert.True(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var cache = new LruCache<string, int>(5);
        cache.Put("a", 1);
        cache.Put("b", 2);
        cache.Put("c", 3);

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.False(cache.TryGet("c", out _));
    }

    [Fact]
    public void Clear_AllowsReuseOfCache()
    {
        var cache = new LruCache<string, int>(2);
        cache.Put("a", 1);
        cache.Put("b", 2);

        cache.Clear();

        cache.Put("c", 3);
        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet("c", out var val));
        Assert.Equal(3, val);
    }

    [Fact]
    public void EvictionOrder_IsCorrect_WithMixedOperations()
    {
        var cache = new LruCache<string, int>(3);

        cache.Put("a", 1);
        cache.Put("b", 2);
        cache.Put("c", 3);

        // Access "a" to promote it; order is now: a, c, b
        cache.TryGet("a", out _);

        // Insert "d" — should evict "b" (least recently used)
        cache.Put("d", 4);

        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.True(cache.TryGet("d", out _));
    }

    [Fact]
    public void IntegerKeys_WorkCorrectly()
    {
        var cache = new LruCache<int, string>(3);
        cache.Put(1, "one");
        cache.Put(2, "two");
        cache.Put(3, "three");

        Assert.True(cache.TryGet(2, out var val));
        Assert.Equal("two", val);
    }

    [Fact]
    public void NullValue_CanBeStoredAndRetrieved()
    {
        var cache = new LruCache<string, string?>(5);
        cache.Put("key", null);

        var found = cache.TryGet("key", out var value);

        Assert.True(found);
        Assert.Null(value);
    }

    [Fact]
    public async Task ConcurrentAccess_DoesNotCorruptState()
    {
        var cache = new LruCache<int, int>(100);
        var tasks = new List<Task>();

        // Multiple threads writing and reading concurrently
        for (int t = 0; t < 10; t++)
        {
            var threadId = t;
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    var key = threadId * 100 + i;
                    cache.Put(key, key);
                    cache.TryGet(key, out _);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Cache should not exceed capacity
        Assert.True(cache.Count <= cache.Capacity);
        Assert.True(cache.Count >= 0);
    }
}
