using Xunit;

namespace Foundgine.Redis.Tests;

/// <summary>Runs a Redis integration test only when the Redis connection is configured.</summary>
public sealed class RedisFactAttribute : FactAttribute
{
    public RedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FOUNDGINE_REDIS")))
            Skip = "Set FOUNDGINE_REDIS to run this Redis integration test.";
    }
}

/// <summary>Runs a Redis theory only when the Redis connection is configured.</summary>
public sealed class RedisTheoryAttribute : TheoryAttribute
{
    public RedisTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FOUNDGINE_REDIS")))
            Skip = "Set FOUNDGINE_REDIS to run this Redis integration theory.";
    }
}
