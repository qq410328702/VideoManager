namespace VideoManager.Services;

/// <summary>
/// A generic thread-safe LRU (Least Recently Used) cache.
/// When the cache reaches its configured capacity, the least recently accessed entry is evicted.
/// 
/// Implementation uses a <see cref="LinkedList{T}"/> for access-order tracking
/// and a <see cref="Dictionary{TKey, TValue}"/> for O(1) lookups.
/// Thread safety is guaranteed via <c>lock</c>.
/// </summary>
/// <typeparam name="TKey">The type of cache keys. Must be non-null.</typeparam>
/// <typeparam name="TValue">The type of cache values.</typeparam>
public class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<CacheEntry>> _map;
    private readonly LinkedList<CacheEntry> _list;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of <see cref="LruCache{TKey, TValue}"/> with the specified capacity.
    /// </summary>
    /// <param name="capacity">The maximum number of entries the cache can hold. Must be greater than zero.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="capacity"/> is less than or equal to zero.</exception>
    public LruCache(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be greater than zero.");

        _capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<CacheEntry>>(capacity);
        _list = new LinkedList<CacheEntry>();
    }

    /// <summary>
    /// Gets the current number of entries in the cache.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _map.Count;
            }
        }
    }

    /// <summary>
    /// Gets the maximum capacity of the cache.
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Attempts to retrieve the value associated with the specified key.
    /// If found, the entry is promoted to the most recently used position.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="value">
    /// When this method returns, contains the value associated with the key if found;
    /// otherwise, the default value for the type.
    /// </param>
    /// <returns><c>true</c> if the key was found in the cache; otherwise, <c>false</c>.</returns>
    public bool TryGet(TKey key, out TValue? value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                // Move to front (most recently used)
                _list.Remove(node);
                _list.AddFirst(node);
                value = node.Value.Value;
                return true;
            }

            value = default;
            return false;
        }
    }

    /// <summary>
    /// Inserts or updates an entry in the cache.
    /// If the key already exists, its value is updated and the entry is promoted to the most recently used position.
    /// If the cache is at capacity, the least recently used entry is evicted before inserting the new entry.
    /// </summary>
    /// <param name="key">The key of the entry.</param>
    /// <param name="value">The value of the entry.</param>
    public void Put(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existingNode))
            {
                // Update existing entry and move to front
                _list.Remove(existingNode);
                existingNode.Value = new CacheEntry(key, value);
                _list.AddFirst(existingNode);
                return;
            }

            // Evict the least recently used entry if at capacity
            if (_map.Count >= _capacity)
            {
                var lruNode = _list.Last!;
                _list.RemoveLast();
                _map.Remove(lruNode.Value.Key);
            }

            // Insert new entry at front (most recently used)
            var newNode = new LinkedListNode<CacheEntry>(new CacheEntry(key, value));
            _list.AddFirst(newNode);
            _map[key] = newNode;
        }
    }

    /// <summary>
    /// Removes the entry with the specified key from the cache.
    /// </summary>
    /// <param name="key">The key of the entry to remove.</param>
    /// <returns><c>true</c> if the entry was found and removed; otherwise, <c>false</c>.</returns>
    public bool Remove(TKey key)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _list.Remove(node);
                _map.Remove(key);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Removes all entries from the cache.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _list.Clear();
            _map.Clear();
        }
    }

    /// <summary>
    /// Internal record to hold key-value pairs in the linked list.
    /// </summary>
    private record struct CacheEntry(TKey Key, TValue Value);
}
