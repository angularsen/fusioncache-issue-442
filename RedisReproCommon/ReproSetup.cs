// ReSharper disable InconsistentNaming
using System.Diagnostics;
using Serilog;
using ZiggyCreatures.Caching.Fusion;

namespace RedisRepro;

public class ReproSetup : IAsyncDisposable
{
    /// <summary>Pre-define a large set of random cache keys.</summary>
    private static readonly Guid[] RandomIds10k = Enumerable.Range(0, 10_000).Select(i => new Guid(i, 0, 0, new byte[8])).ToArray();

    private readonly List<IDisposable> _disposables = new();
    private readonly List<IAsyncDisposable> _asyncDisposables = new();
    private readonly ILogger _logger;

    public ReproSetup(ILogger? logger = null)
    {
        _logger = (logger ?? Log.Logger).ForContext<ReproSetup>();
    }

    /// <summary>
    ///     Creates a FusionCache instances with its own Redis connection multiplexer.
    ///     To create multiple instances reusing a single Redis multiplexer, call <see cref="CreateFactory"/> once and create instances off of it.
    /// </summary>
    /// <param name="name">Name for FusionCache and the Redis log file. Defaults to random name.</param>
    /// <returns></returns>
    public IFusionCache CreateFusionCacheWithItsOwnRedisMultiplexer(string? name = null)
    {
        name ??= GetRandomName(); // Random name is useful to write to different Redis log files, and to distinguish instances when debugging.
        IFusionCache fusionCache = CreateFactory(cacheName: name)
            .CreateFusionCacheWithSharedRedisMultiplexer(cacheName: name);
        _disposables.Add(fusionCache);

        return fusionCache;
    }

    private FusionCacheFactory CreateFactory(string cacheName)
    {
        ArgumentNullException.ThrowIfNull(cacheName);

        var factory = new FusionCacheFactory(logger: _logger, cacheName: cacheName);
        _asyncDisposables.Add(factory);
        return factory;
    }

    public static string GetRandomName() => "fusioncache-" + GetRandomShortString();
    private static string GetRandomShortString() => Guid.NewGuid().ToString()[..5];

    public async ValueTask DisposeAsync()
    {
        foreach (var ad in _asyncDisposables)
        {
            await ad.DisposeAsync();
        }

        foreach (var d in _disposables)
        {
            d.Dispose();
        }
    }

    /// <summary>
    ///     1ms duration (force going to Redis to fetch new value)<br />
    ///     5m fail-safe duration (rarely execute factory, we are mainly exercising backplane and distributed cache operations)
    /// </summary>
    private FusionCacheEntryOptions CacheOptions { get; } = new FusionCacheEntryOptions()
        .SetDurationMs(1)
        .SetFailSafe(true, TimeSpan.FromMinutes(5));

    private List<CacheEntry> GetFreshEntries(int count = 800)
    {
        return RandomIds10k
            .Take(count)
            .Select(id => new CacheEntry
                {
                    UserId = id,
                    Value1 = Random.Shared.Next(0, 100),
                    Value3 = Random.Shared.Next(0, 100),
                    Value4 = Random.Shared.Next(0, 100),
                    Value2 = Random.Shared.Next(0, 100),
                }
            ).ToList();
    }

    public async Task<List<CacheEntry>> ExerciseSetAsync(IFusionCache fusionCache, TimeSpan? setDelay = null,
        CancellationToken ct = default)
    {
        int counter = 0;
        var totalElapsed = TimeSpan.Zero;
        var maxElapsed = TimeSpan.Zero;
        var sw = Stopwatch.StartNew();

        List<CacheEntry> freshEntries = GetFreshEntries();
        foreach (CacheEntry freshEntry in freshEntries)
        {
            sw.Restart();
            await fusionCache.SetAsync(
                key: $"repro:user:{freshEntry.UserId}",
                value: freshEntry,
                options: CacheOptions,
                token: ct);

            TimeSpan elapsed = sw.Elapsed;
            counter++;
            totalElapsed += elapsed;
            if (elapsed > maxElapsed) maxElapsed = elapsed;

            if (counter % 1000 == 0) LogProgress();

            if (setDelay > TimeSpan.Zero)
                await Task.Delay(setDelay.Value, ct);
        }

        LogProgress();
        return freshEntries;

        void LogProgress() => _logger.Information(
            "FusionCache({CacheName}).SetAsync {Counter} times, avg {TotalElapsedMs:F3} ms, max {MaxElapsedMs:F3} ms",
            fusionCache.CacheName, counter, totalElapsed.TotalMilliseconds / counter, maxElapsed.TotalMilliseconds);
    }

    public async Task<List<CacheEntry>> ExerciseGetOrSetAsync(IFusionCache fusionCache, TimeSpan? factoryDelay = null,
        CancellationToken ct = default)
    {
        int counter = 0;
        var totalElapsed = TimeSpan.Zero;
        var maxElapsed = TimeSpan.Zero;
        var sw = Stopwatch.StartNew();

        List<CacheEntry> freshEntries = GetFreshEntries();
        foreach (CacheEntry freshEntry in freshEntries)
        {
            sw.Restart();
            await fusionCache.GetOrSetAsync<CacheEntry>(
                key: $"repro:user:{freshEntry.UserId}",
                factory: async _ =>
                {
                    if (factoryDelay > TimeSpan.Zero)
                        await Task.Delay(factoryDelay.Value, ct);

                    return freshEntry;
                },
                options: CacheOptions,
                token: ct);

            counter++;
            var elapsed = sw.Elapsed;
            totalElapsed += elapsed;
            if (elapsed > maxElapsed) maxElapsed = elapsed;

            if (counter % 1000 == 0) LogProgress();
        }

        LogProgress();
        return freshEntries;

        void LogProgress() => Log.Information(
            "FusionCache({CacheName}).GetOrSetAsync {Counter} times, avg {TotalElapsedMs:F3} ms, max {MaxElapsedMs:F3} ms",
            fusionCache.CacheName, counter, totalElapsed.TotalMilliseconds / counter, maxElapsed.TotalMilliseconds);
    }

    public async Task ExerciseGetOrSetAsync_UntilCanceled(IFusionCache fusionCache, TimeSpan? factoryDelay = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await ExerciseGetOrSetAsync(fusionCache, factoryDelay, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }

        _logger.Information("Stopped ExerciseGetOrSetAsync for {FusionCacheName} after {ElapsedSeconds:F3} s",
            fusionCache.CacheName, sw.Elapsed.TotalSeconds);
    }

    public async Task ExerciseSetAsync_UntilCanceled(IFusionCache fusionCache, TimeSpan? setDelay = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await ExerciseSetAsync(fusionCache, setDelay, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }

        _logger.Information("Stopped ExerciseSetAsync for {FusionCacheName} after {ElapsedSeconds:F3} s",
            fusionCache.CacheName, sw.Elapsed.TotalSeconds);
    }
}