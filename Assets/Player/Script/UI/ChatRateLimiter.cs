using System;
using System.Collections.Generic;

namespace BattlePvp.UI
{
    /// <summary>Per-connection chat budget; rejected packets cannot reset the refill clock.</summary>
    public sealed class ChatRateLimiter<T> where T : class
    {
        public const int BurstCapacity = 3;
        public const double MessagesPerSecond = 1d;
        private struct Budget
        {
            public double Tokens;
            public double UpdatedAt;
        }
        private readonly Dictionary<T, Budget> _budgets = new Dictionary<T, Budget>();
        public int Count => _budgets.Count;

        public bool TryConsume(T connection, double now)
        {
            if (connection == null || !double.IsFinite(now)) return false;
            if (!_budgets.TryGetValue(connection, out Budget budget))
                budget = new Budget { Tokens = BurstCapacity, UpdatedAt = now };
            if (now < budget.UpdatedAt) return false;
            budget.Tokens = Math.Min(BurstCapacity, budget.Tokens + (now - budget.UpdatedAt) * MessagesPerSecond);
            budget.UpdatedAt = now;
            bool accepted = budget.Tokens >= 1d;
            if (accepted) budget.Tokens -= 1d;
            _budgets[connection] = budget;
            return accepted;
        }

        public void Remove(T connection) { if (connection != null) _budgets.Remove(connection); }
        public void Clear() => _budgets.Clear();
    }
}
