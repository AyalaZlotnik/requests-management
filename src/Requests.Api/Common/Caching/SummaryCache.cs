using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Requests.Api.Common.Caching;

public class CacheOptions
{
    public int SummaryTtlSeconds { get; set; } = 60;
}

/// <summary>
/// In-memory cache for the dashboard summary.
/// Expiration: absolute TTL (configurable). Invalidation: every successful status change calls
/// <see cref="Invalidate"/>. Entries are tied to a cancellation token captured *before* the value
/// is computed, so a value computed concurrently with an update is evicted immediately instead of
/// being cached stale.
/// Registered as a singleton because it owns the invalidation token.
/// </summary>
public sealed partial class SummaryCache(IMemoryCache cache, IOptions<CacheOptions> options, ILogger<SummaryCache> logger)
    : IDisposable
{
    private const string Key = "requests:summary";
    private CancellationTokenSource _invalidation = new();

    public async Task<T> GetOrCreateAsync<T>(Func<CancellationToken, Task<T>> factory, CancellationToken ct)
    {
        if (cache.TryGetValue(Key, out T? cached) && cached is not null)
        {
            return cached;
        }

        var token = new CancellationChangeToken(Volatile.Read(ref _invalidation).Token);
        var value = await factory(ct);

        cache.Set(Key, value, new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromSeconds(options.Value.SummaryTtlSeconds))
            .AddExpirationToken(token));
        return value;
    }

    public void Invalidate()
    {
        var previous = Interlocked.Exchange(ref _invalidation, new CancellationTokenSource());
        previous.Cancel();
        previous.Dispose();
        LogInvalidated(logger);
    }

    public void Dispose() => _invalidation.Dispose();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Summary cache invalidated")]
    private static partial void LogInvalidated(ILogger logger);
}
