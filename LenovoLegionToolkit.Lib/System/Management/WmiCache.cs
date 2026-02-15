using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.System.Management;

/// <summary>
/// Thread-safe cache for WMI query results.
/// Only use for static data (device info, capabilities) - NOT for real-time data (temps, power).
/// </summary>
public static class WmiCache
{
    private readonly struct CacheEntry
    {
        public object Value { get; init; }
        public DateTime ExpiresAt { get; init; }
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    }

    private static readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    /// <summary>
    /// Default TTL for static hardware info (essentially forever until app restart)
    /// </summary>
    public static readonly TimeSpan StaticDataTtl = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets cached value or creates it using the factory function.
    /// </summary>
    /// <typeparam name="T">Type of cached value</typeparam>
    /// <param name="cacheKey">Unique key for this query</param>
    /// <param name="factory">Function to create the value if not cached</param>
    /// <param name="ttl">Time-to-live, defaults to StaticDataTtl</param>
    /// <returns>Cached or newly created value</returns>
    public static async Task<T> GetOrCreateAsync<T>(
        string cacheKey,
        Func<Task<T>> factory,
        TimeSpan? ttl = null)
    {
        // Check if cached and not expired
        if (_cache.TryGetValue(cacheKey, out var entry) && !entry.IsExpired)
        {
            return (T)entry.Value;
        }

        // Create new value
        var value = await factory().ConfigureAwait(false);

        // Cache it
        var newEntry = new CacheEntry
        {
            Value = value!,
            ExpiresAt = DateTime.UtcNow + (ttl ?? StaticDataTtl)
        };

        _cache[cacheKey] = newEntry;

        return value;
    }

    /// <summary>
    /// Invalidates a specific cache entry
    /// </summary>
    public static void Invalidate(string cacheKey)
    {
        _cache.TryRemove(cacheKey, out _);
    }

    /// <summary>
    /// Invalidates all cache entries matching a prefix
    /// </summary>
    public static void InvalidateByPrefix(string prefix)
    {
        foreach (var key in _cache.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                _cache.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// Clears all cached data
    /// </summary>
    public static void Clear()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Gets current cache size (for diagnostics)
    /// </summary>
    public static int Count => _cache.Count;
}
