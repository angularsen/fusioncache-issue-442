# Repro Redis timeouts

`SetAsync` and `GetOrAsync` gets blocked in certain conditions, and results in the API no longer responding to requests or responding much slower than usual.
To reproduce, multiple API instances are required, with a backplane to communicate changes to the distributed cache. The backplane events seem central to the problem, since it triggers `HMGET` operations to proactively refresh each API's local memory copy.

This was a regression from FusionCache `2.0.0` to `2.0.1/2.1.0`, but seems fixed in `2.2.0-preview1`. Not sure exactly what changed between these yet.
Upgrading to FusionCache `2.2.0-preview1` seems to fix the timeouts and significantly improve responsiveness of API requests.

Two repro apps are included:
- RedisReproApi - hosting endpoints to invoke `SetAsync` or `GetOrSetAsync`
- RedisReproConsole - console app, trying to do the same thing with multiple `FusionCache` instances invoked concurrently

I have so far only been able to reproduce in an API request context, not in the console app.

## Reproduce with FusionCache 2.1.0

To quickly reproduce, run these two scripts in _separate_ bash shells:

```sh
# Run 3 API instances with ports 5800, 5801, 5802
./run-apis.sh
```

```sh
# Run concurrent HTTP requests against the 3 APIs invoking `SetAsync` with _unique_ cache names.
# Timeouts NOT expected.
./run-requests-setasync-unique-cachenames.sh

# Then run a similar test, but with the _same_ FusionCache name for all 3 API instances.
# Timeouts are expected, due to using the same name? See examples below.
./run-requests-setasync-same-cachename.sh
```

### Video of repro
#### RedisRepro - FusionCache 2.1.0 and timeouts
[![RedisRepro - FusionCache 2.1.0 and timeouts](https://img.youtube.com/vi/dAQI_Mn47uE/0.jpg)](https://youtu.be/dAQI_Mn47uE)

#### RedisRepro - FusionCache 2.2.0-preview1 and no timeouts
[![RedisRepro - FusionCache 2.2.0-preview1 and no timeouts](https://img.youtube.com/vi/ebUsSFl2R7w/0.jpg)](https://youtu.be/ebUsSFl2R7w)


### How to reproduce in general, with an API endpoint
- Configure a FusionCache singleton instance with `StackExchange.Redis 2.8.31` for both distributed cache and backplane
- Run multiple API instances (3 seems to reproduce more easily than 2, `./run-apis.sh` does this)
  - The endpoint should invoke either `SetAsync` or `GetOrSetAsync` many times for a large set of cache keys (repro sets 800 keys)
  - The endpoint should use a `FusionCache` singleton instance with the same **CacheName**, unique names does not reproduce
    - `GET /repro/setAsync?useRandomCacheName=true` in [ReproEndpoint.cs](ReproEndpoint.cs), with a uniquely named FusionCache singleton instance (no timeouts)
    - `GET /repro/setAsync?useRandomCacheName=false` in [ReproEndpoint.cs](ReproEndpoint.cs), with a fixed-name FusionCache singleton instance (timeouts)
- Send concurrent requests to all API instances to trigger backplane events, which notifies the other instances about changes to cache (`./run-requests-setasync-same-cachename.sh` does this)
- Observe that API requests stops responding or become very slow
- Observe that upgrading FusionCache to `2.2.0-preview1` fixes or at least significantly improves the situation

### Console app - Unable to reproduce

So far I have been unable to reproduce outside an API request context, neither with unit test or a plain console app.

I tried creating a unit test with multiple `FusionCache` instances using separate Redis connection multiplexers to resemble separate API instances.
Not sure if `Task` and `async` works differently in an aspnetcore request context somehow. 

### Example - Redis timeouts in the API instances

![Redis timeouts in API instances](images/redis_timeouts_same_cachename.png)

In the API console output:
```log
[15:26:28 INF] ├ expire from cache level (0.001 ms)
[15:26:28 INF] ├ expire from cache level (0.001 ms)
[15:26:28 INF] └─ receive from backplane (2029.266 ms)
[15:26:28 INF] ├ expire from cache level (0.005 ms)
[15:26:28 INF] └─ receive from backplane (5007.115 ms)
[15:26:28 INF] ├ expire from cache level (0.001 ms)
[15:26:28 INF] ├ expire from cache level (0 ms)
[15:26:28 INF] └─ receive from backplane (5006.402 ms)
[15:26:28 INF] │ Redis cache connection configuration changed
[15:26:28 ERR] ├ get from cache level (5000.559 ms)
StackExchange.Redis.RedisTimeoutException: The message timed out in the backlog attempting to send because no connection became available, command=HMGET, timeout: 5000, inst: 0, qu: 22, qs: 0, aw: False, bw: SpinningDown, rs: ComputeResult, ws: Idle, in: 0, in-pipe: 1248, out-pipe: 0, last-in: 0, cur-in: 23, sync-ops: 2705, async-ops: 9600, serverEndpoint: localhost:6379, conn-sec: n/a, aoc: 0, mc: 1/1/0, mgr: 9 of 10 available, clientName: Andreas-mac(SE.Redis-v2.8.31.52602), IOCP: (Busy=0,Free=1000,Min=1,Max=1000), WORKER: (Busy=45,Free=32722,Min=10,Max=32767), POOL: (Threads=69,QueuedItems=0,CompletedItems=27794,Timers=4), v: 2.8.31.52602 (Please take a look at this article for some common client-side issues that can cause timeouts: https://stackexchange.github.io/StackExchange.Redis/Timeouts)
   at StackExchange.Redis.ConnectionMultiplexer.ExecuteSyncImpl[T](Message message, ResultProcessor`1 processor, ServerEndPoint server, T defaultValue) in /_/src/StackExchange.Redis/ConnectionMultiplexer.cs:line 2047
   at StackExchange.Redis.RedisDatabase.HashGet(RedisKey key, RedisValue[] hashFields, CommandFlags flags) in /_/src/StackExchange.Redis/RedisDatabase.cs:line 484
   at Microsoft.Extensions.Caching.StackExchangeRedis.RedisCache.GetAndRefresh(String key, Boolean getData)
   at ZiggyCreatures.Caching.Fusion.Internals.RunUtils.RunSyncFuncWithTimeout[TResult](Func`2 syncFunc, TimeSpan timeout, Boolean cancelIfTimeout, Action`1 timedOutTaskProcessor, CancellationToken token) in /_/src/ZiggyCreatures.FusionCache/Internals/RunUtils.cs:line 240
   at ZiggyCreatures.Caching.Fusion.Internals.Distributed.DistributedCacheAccessor.TryGetEntry[TValue](String operationId, String key, FusionCacheEntryOptions options, Boolean hasFallbackValue, Nullable`1 timeout, CancellationToken token)
[15:26:28 INF] ├ expire from cache level (0.001 ms)
[15:26:28 INF] └─ receive from backplane (5007.744 ms)
[15:26:28 INF] ├ expire from cache level (0.001 ms)
[15:26:28 INF] └─ receive from backplane (5006.615 ms)
[15:26:28 INF] └─ receive from backplane (5006.265 ms)
[15:26:28 INF] ├ expire from cache level (0 ms)
[15:26:28 INF] └─ receive from backplane (5006.117 ms)
```

### Example - Unresponsive API requests

In the `./run-requests-setasync-same-cachename.sh` console output:
```log
Begin 3x loadtest /repro/setAsync
- APIs: localhost:5800, localhost:5801, localhost:5802
- Max requests per API: 10
- Concurrency per API: 10

[...]
Requests: 139, requests per second: 6, mean latency: 1859 ms
Requests: 20, requests per second: 2, mean latency: 10008.6 ms
Errors: 10, accumulated errors: 20, 100% of total requests
Requests: 20, requests per second: 2, mean latency: 10012.9 ms
Errors: 10, accumulated errors: 20, 100% of total requests
Requests: 140, requests per second: 6, mean latency: 1873.8 ms
Requests: 20, requests per second: 2, mean latency: 10016.1 ms
Errors: 10, accumulated errors: 20, 100% of total requests
Requests: 170, requests per second: 6, mean latency: 1639.1 ms

Target URL:          http://localhost:5800/repro/setAsync?useRandomCacheName=false
Max time (s):        30
Concurrent clients:  50
Running on cores:    5
Agent:               none

Completed requests:  100
Total errors:        100
Total time:          30.153 s
Mean latency:        10078.5 ms
Effective rps:       3

Percentage of requests served within a certain time
  50%      10029 ms
  90%      10177 ms
  95%      10180 ms
  99%      10210 ms
 100%      10210 ms (longest request)

   -1:   100 errors

Target URL:          http://localhost:5801/repro/setAsync?useRandomCacheName=false
Max time (s):        30
Concurrent clients:  50
Running on cores:    5
Agent:               none

Completed requests:  850
Total errors:        0
Total time:          30.188 s
Mean latency:        1750 ms
Effective rps:       28

Percentage of requests served within a certain time
  50%      1655 ms
  90%      2448 ms
  95%      2598 ms
  99%      2630 ms
 100%      2635 ms (longest request)

Target URL:          http://localhost:5802/repro/setAsync?useRandomCacheName=false
Max time (s):        30
Concurrent clients:  50
Running on cores:    5
Agent:               none

Completed requests:  100
Total errors:        100
Total time:          30.321 s
Mean latency:        10039.9 ms
Effective rps:       3

Percentage of requests served within a certain time
  50%      10030 ms
  90%      10075 ms
  95%      10115 ms
  99%      10222 ms
 100%      10222 ms (longest request)

   -1:   100 errors
```

