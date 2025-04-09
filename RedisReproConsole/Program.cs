// Set to false to mimic ./run-requests-setasync-unique-cachenames.sh, which does not get timeouts.
// Set to true to mimic ./run-requests-setasync-same-cachename.sh, which gets timeouts with multiple API instances, but we can't reproduce in a single console app..?
const bool useSameCacheName = false;
const int cacheInstanceCount = 3;

// ReSharper disable AccessToDisposedClosure
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft.AspNetCore.Hosting", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Routing", LogEventLevel.Warning)
    // .MinimumLevel.Override("ZiggyCreatures.Caching.Fusion", LogEventLevel.Warning) // Information includes successful cache/backplane operations
    .Enrich.WithProperty("Application", "RedisRepro")
    .WriteTo.Console(Formatters.CreateConsoleTextFormatter(theme: TemplateTheme.Code), LogEventLevel.Warning) // Only error/warning in console, use Seq to view all events.
    .WriteTo.Seq(Environment.GetEnvironmentVariable("SEQ_URL") ?? "http://localhost:5341")
    .CreateLogger();

using var listener = new ActivityListenerConfiguration()
    .Instrument.AspNetCoreRequests()
    .TraceToSharedLogger();

Console.WriteLine($"Begin repro with {cacheInstanceCount}x FusionCache instances with individual Redis multiplexers");
await using ReproSetup setup = new();
IFusionCache[] cacheInstances = Enumerable.Range(0, cacheInstanceCount)
    .Select(i => setup.CreateFusionCacheWithItsOwnRedisMultiplexer(useSameCacheName ? "sameCacheName" : $"cache{i}"))
    .ToArray();

using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
CancellationToken stopToken = stopTimeout.Token;
TimeSpan delayPerSetAsync = TimeSpan.Zero;
var sw = Stopwatch.StartNew();

try
{
    // var taskFactory = new TaskFactory(TaskCreationOptions.LongRunning, TaskContinuationOptions.None);
    var tasks = cacheInstances
        // .Select(cache =>
        //     taskFactory.StartNew(
        //         () => setup.ExerciseSetAsync_UntilCanceled(cache, delayPerSetAsync, stopToken), stopToken));
        .Select(cache =>
            Task.Run(() => setup.ExerciseSetAsync_UntilCanceled(cache, delayPerSetAsync, stopToken), stopToken));

    await Task.WhenAll(tasks);
}
finally
{
    Log.Information("Completed all tasks in {ElapsedSeconds:F3} s", sw.Elapsed.TotalSeconds);
    await Log.CloseAndFlushAsync();
}