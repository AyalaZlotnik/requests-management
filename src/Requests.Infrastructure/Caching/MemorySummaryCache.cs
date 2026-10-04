using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Requests.Application.Requests.Abstractions;
using Requests.Application.Requests.Contracts;

namespace Requests.Infrastructure.Caching;

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
public sealed partial class MemorySummaryCache(IMemoryCache cache, IOptions<CacheOptions> options, ILogger<MemorySummaryCache> logger)
    : ISummaryCache, IDisposable
{
    private const string Key = "requests:summary";
    private CancellationTokenSource _invalidation = new();

    public async Task<RequestsSummaryDto> GetOrCreateAsync(Func<CancellationToken, Task<RequestsSummaryDto>> factory, CancellationToken ct)
    {
        if (cache.TryGetValue(Key, out RequestsSummaryDto? cached) && cached is not null)
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
        // Cancel only. A summary read running in parallel may have just taken this source and be about to
        // read its Token – after Dispose() that would throw ObjectDisposedException. A cancelled source holds
        // no unmanaged resources here, so leaving it to the garbage collector is safe.
        previous.Cancel();
        LogInvalidated(logger);
    }

    public void Dispose() => _invalidation.Dispose();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Summary cache invalidated")]
    private static partial void LogInvalidated(ILogger logger);
}
