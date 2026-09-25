using System;
using BattlePvp.UI;

internal static class ChatPermissionRegression
{
    internal static void Run(Action<bool, string> require)
    {
        var limiter = new ChatRateLimiter<object>();
        var first = new object();
        var second = new object();
        require(!limiter.TryConsume(null, 10d), "Missing connections cannot create a chat budget.");
        require(!limiter.TryConsume(first, double.NaN), "NaN cannot initialize a budget.");
        require(!limiter.TryConsume(first, double.PositiveInfinity), "Infinity cannot initialize a budget.");
        require(limiter.Count == 0, "Invalid input does not allocate a connection entry.");
        for (int i = 0; i < ChatRateLimiter<object>.BurstCapacity; i++)
            require(limiter.TryConsume(first, 10d), "A normal short chat burst is allowed.");
        require(!limiter.TryConsume(first, 10d), "A burst cannot exceed three messages.");
        for (int i = 0; i < 100; i++)
            require(!limiter.TryConsume(first, 10.5d), "Repeated rejected packets cannot refill tokens.");
        require(!limiter.TryConsume(first, 9d), "A backwards clock cannot refill the budget.");
        require(limiter.TryConsume(first, 11d), "Partial refill survives repeated rejections.");
        require(!limiter.TryConsume(first, 11d), "One second restores exactly one message.");
        require(limiter.TryConsume(second, 11d), "Another participant has an independent budget.");
        require(limiter.Count == 2, "Only participating connections are tracked.");
        for (int i = 0; i < ChatRateLimiter<object>.BurstCapacity; i++)
            require(limiter.TryConsume(first, 1000d), "Idle time restores at most a full burst.");
        require(!limiter.TryConsume(first, 1000d), "Long idle time does not accumulate unlimited messages.");
        limiter.Remove(first);
        require(limiter.Count == 1, "Disconnect releases that connection's budget.");
        require(limiter.TryConsume(first, 1000d), "A new connection lifetime starts with a normal budget.");
        limiter.Remove(null);
        limiter.Clear();
        require(limiter.Count == 0, "Server shutdown releases all budgets.");
        require(limiter.TryConsume(second, 1d), "A restarted server clock can initialize a new session.");
    }
}
