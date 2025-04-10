using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;
using ILogger = Serilog.ILogger;

namespace RedisRepro;

/// <summary>
///     Creates FusionCache instances with a single, shared Redis connection multiplexer.
/// </summary>
/// <remarks>
///     To create multiple Redis connection multiplexer instances, create separate factories.
/// </remarks>
public sealed class FusionCacheFactory : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly Task<ConnectionMultiplexer> _redisMultiplexer;
    private readonly StreamWriter _redisLogFileWriter;

    public FusionCacheFactory(ILogger logger, string cacheName)
    {
        _logger = logger;

        string tempFileName = Path.Combine(Path.GetTempPath(), $"redis-log-{cacheName}-{GetRandomShortString()}.txt"); // Unique log file for each multiplexer
        if (File.Exists(tempFileName)) File.Delete(tempFileName);

        _logger.Information("Redis logfile: {TempFileName}", tempFileName);
        _redisLogFileWriter = new StreamWriter(File.OpenWrite(tempFileName), leaveOpen: false);

        var redisConfig = ConfigurationOptions.Parse("localhost:6379");
        redisConfig.AbortOnConnectFail = false; // Reconnect if disconnected
        redisConfig.ClientName = cacheName + GetRandomShortString(); // cacheName is also used for FusionCache.CacheName, so make this unique to exclude it from possible causes where Redis timeouts are observed when cache name is shared across API instances.

        _redisMultiplexer = GetSharedRedisMultiplexerWithLogging(redisConfig, _redisLogFileWriter);
    }

    /// <summary>
    ///     Creates a FusionCache instance.
    /// </summary>
    /// <param name="cacheName">The cache name. Defaults to random name.</param>
    /// <returns></returns>
    public IFusionCache CreateFusionCacheWithSharedRedisMultiplexer(string? cacheName = null)
    {
        return new FusionCache(Microsoft.Extensions.Options.Options.Create(new FusionCacheOptions
            {
                SerializationErrorsLogLevel = LogLevel.Warning,
                CacheName = cacheName ?? ReproSetup.GetRandomName(),
            }))
            .SetupSerializer(new FusionCacheSystemTextJsonSerializer(new JsonSerializerOptions(RedisJsonOptions)))
            .SetupDistributedCache(new RedisCache(Microsoft.Extensions.Options.Options.Create(new RedisCacheOptions()
            {
                ConnectionMultiplexerFactory = async () => await _redisMultiplexer,
            })))
            .SetupBackplane(new RedisBackplane(Microsoft.Extensions.Options.Options.Create(new RedisBackplaneOptions
            {
                ConnectionMultiplexerFactory = async () => await _redisMultiplexer,
            })));
    }

    public async ValueTask DisposeAsync()
    {
        await _redisLogFileWriter.DisposeAsync();
    }

    private Task<ConnectionMultiplexer> GetSharedRedisMultiplexerWithLogging(ConfigurationOptions redisConfig, StreamWriter redisLogFile)
    {
        return Task.Run(async () =>
        {
            ConnectionMultiplexer cm = await ConnectionMultiplexer.ConnectAsync(redisConfig, log: redisLogFile);
            LogConnectionEvents(cm, _logger);
            return cm;
        });
    }

    private static string GetRandomShortString() => Guid.NewGuid().ToString()[..5];
    private static void LogConnectionEvents(ConnectionMultiplexer connection, ILogger logger)
    {
        connection.ConnectionFailed += (_, e) => logger.Warning(e.Exception, "Redis cache connection failed");
        connection.ConnectionRestored += (_, _) => logger.Information("Redis cache connection restored");
        connection.ErrorMessage += (_, e) => logger.Warning("Redis cache connection error: {Message}", e.Message);
        connection.InternalError += (_, e) => logger.Error(e.Exception, "Redis cache connection internal error");
        connection.ConfigurationChanged += (_, _) => logger.Information("Redis cache connection configuration changed");
        connection.ConfigurationChangedBroadcast += (_, _) => logger.Information("Redis cache connection configuration changed by broadcast");
        connection.HashSlotMoved += (_, e) => logger.Information("Redis cache connection hash slot moved from {RedisOldEndpoint} to {RedisNewEndpoint}", e.OldEndPoint?.ToString(), e.NewEndPoint.ToString());

        string connectionStatus = connection.IsConnected ? "Connected" : "Disconnected";
        string endpoints = string.Join(", ", connection.GetEndPoints().Select(e => e.ToString()));
        logger.Information("Redis cache connection {RedisConnectionName} is {RedisConnectionStatus} to {RedisConnectionEndpoint}",
            connection.ToString(), connectionStatus, endpoints);
    }

    private static JsonSerializerOptions RedisJsonOptions => new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
