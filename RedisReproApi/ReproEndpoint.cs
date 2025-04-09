using Microsoft.AspNetCore.Mvc;

namespace RedisRepro;

public static class ReproEndpoint
{
    private static readonly ReproSetup Setup = new();
    private static readonly IFusionCache SingletonCacheFixedName = Setup.CreateFusionCacheWithItsOwnRedisMultiplexer("SingletonCache");
    private static readonly IFusionCache SingletonCacheRandomName = Setup.CreateFusionCacheWithItsOwnRedisMultiplexer(ReproSetup.GetRandomName());

    public static void MapReproApi(WebApplication webApplication)
    {
        webApplication.MapGet("/repro/setAsync", async ([FromQuery] bool useRandomCacheName = true, CancellationToken ct = default) =>
            {
                IFusionCache fusionCache = useRandomCacheName ? SingletonCacheRandomName : SingletonCacheFixedName;
                var setDelay = TimeSpan.Zero;

                List<CacheEntry> entries = await Setup.ExerciseSetAsync(fusionCache, setDelay, ct);

                return new { UserCount = entries.Count, };
            })
            .WithName("Repro with setAsync");

        webApplication.MapGet("/repro/getOrSetAsync", async ([FromQuery] bool useRandomCacheName = true, CancellationToken ct = default) =>
            {
                IFusionCache fusionCache = useRandomCacheName ? SingletonCacheRandomName : SingletonCacheFixedName;
                var factoryDelay = TimeSpan.Zero;

                List<CacheEntry> entries = await Setup.ExerciseGetOrSetAsync(fusionCache, factoryDelay, ct);

                return new { UserCount = entries.Count };
            })
            .WithName("Repro with getOrSetAsync");
    }
}