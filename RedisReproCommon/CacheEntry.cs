namespace RedisRepro;

/// <summary>Small cache entry, just serialize some JSON.</summary>
public sealed record CacheEntry
{
    public Guid UserId { get; init; }
    public int Value1 { get; init; }
    public int Value2 { get; init; }
    public int Value3 { get; init; }
    public double Value4 { get; init; }
}