using System;
using System.Collections.Generic;
using BattlePvp.Combat;

internal static class CombatExecutionRegression
{
    internal static void Run(Action<bool, string> require)
    {
        var state = default(CombatSkillExecution);
        require(state.TryBegin(10d, 9.9d, 0.7d, 35d, out state), "A normal accepted cast must begin.");
        require(state.IsCasting && Math.Abs(state.CastCompleteAt - 10.6d) < 0.00001d && state.CooldownUntil == 44.9d,
            "Accepted rewind time must determine cast completion and cooldown.");
        require(!state.TryBegin(11d, 11d, 1d, 35d, out _), "Casting blocks a duplicate activation.");
        require(!state.TryActivate(10.6d, 10d, out _), "The cast must finish before the active effect begins.");
        state = state.FinishCast();
        require(state.TryActivate(10.6d, 10d, out state), "An accepted completed cast activates normally.");
        require(state.ActiveUntil == 20.6d && !state.CanBegin(15d), "The active effect blocks reuse.");
        state = state.EndActive();
        require(state.ActiveUntil == 0d && state.CooldownUntil == 44.9d, "Active expiry must preserve cooldown.");
        require(!state.CanBegin(44.899d) && state.CanBegin(44.9d), "Cooldown expires at its exact deadline.");
        var cancelled = new CombatSkillExecution(true, 11d, 20d, 45d).Cancel();
        require(!cancelled.IsCasting && cancelled.CastCompleteAt == 0d && cancelled.ActiveUntil == 0d,
            "Death, interruption and disable must clear execution state.");
        require(cancelled.CooldownUntil == 45d && cancelled.Cancel().CooldownUntil == 45d && !cancelled.CanBegin(44d),
            "Repeated cancellation must not refund an accepted cooldown.");
        var idle = new CombatSkillExecution(false, 0d, 0d, 5d);
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
        {
            require(!idle.TryBegin(5d, 5d, invalid, 35d, out CombatSkillExecution rejected) && rejected.CooldownUntil == 5d,
                "Invalid cast duration must leave the previous state unchanged.");
            require(!idle.TryBegin(5d, 5d, 1d, invalid, out rejected) && !rejected.IsCasting,
                "Invalid cooldown duration must not begin a cast.");
            require(!idle.TryActivate(5d, invalid, out rejected) && rejected.ActiveUntil == 0d,
                "Invalid active duration must not create an effect.");
        }
        require(!idle.TryBegin(double.NaN, 5d, 1d, 35d, out _) && !idle.TryBegin(5d, double.NaN, 1d, 35d, out _),
            "Invalid clock input cannot produce accepted action deadlines.");
        require(!idle.TryBegin(5d, double.MaxValue, double.MaxValue, 1d, out _),
            "Deadline overflow must be rejected.");

        var locks = new CombatActionLocks();
        locks.LockUntil(CombatCastChannel.Strength, 2d);
        locks.LockUntil(CombatCastChannel.Advanced, 5d);
        require(locks.IsLocked(2d) && !locks.IsLocked(5d), "Independent cast locks release at their own deadlines.");
        locks.LockAnimationUntil(51d);
        require(locks.IsLocked(50d) && !locks.IsLocked(51d), "Animation locks expire even if presentation never completes.");
        locks.Cancel();
        require(!locks.IsLocked(0d), "Cancellation releases all predicted action locks.");
        locks.LockUntil(CombatCastChannel.Agility, double.NaN);
        require(!locks.IsLocked(0d), "An invalid predicted lock must not block actions.");

        CombatOwnerAction ownerAction = CombatOwnerAction.Begin(2, 10d, 7, 0.75d).WithAnimation(1.333d);
        require(ownerAction.SkillKey == 2 && ownerAction.StartedAt == 10d && ownerAction.InputFlags == 7,
            "Accepted controls must retain animation identity, start time and authored input flags.");
        require(ownerAction.HasAnimation(10.5d) && ownerAction.RemainingInput(10.5d) == 0.25d,
            "Rejoining after the cast ends must preserve remaining animation and input locks.");
        require(ownerAction.HasAnimation(10.75d) && ownerAction.RemainingInput(10.75d) == 0d,
            "Input expiry must not erase the longer animation attack lock.");
        require(!ownerAction.HasAnimation(ownerAction.AnimationUntil), "The authored animation lock expires at its exact deadline.");
        require(!CombatOwnerAction.Cancelled.HasAnimation(10d) && CombatOwnerAction.Cancelled.RemainingInput(10d) == 0d,
            "Cancellation must clear both accepted controls.");
        CombatOwnerAction instant = CombatOwnerAction.Begin(22, 10d, 1, 0.15d);
        require(!instant.HasAnimation(10d) && Math.Abs(instant.RemainingInput(10d) - 0.15d) < 0.00001d,
            "An instant skill can have a short input lock without an animation.");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1d })
        {
            require(!instant.WithAnimation(invalid).HasAnimation(10d), "Invalid animation duration must not create an owner lock.");
            require(CombatOwnerAction.Begin(2, 10d, 7, invalid).SkillKey == -1,
                "Invalid input duration must not create accepted owner controls.");
        }
        require(CombatOwnerAction.Begin(2, double.NaN, 7, 1d).SkillKey == -1 &&
                CombatOwnerAction.Begin(2, double.MaxValue, 7, double.MaxValue).SkillKey == -1,
            "Invalid or overflowing absolute deadlines must reject accepted controls.");
        CombatOwnerAction reconnect = CombatOwnerAction.Begin(2, 1999d, 7, 0.75d).WithAnimation(1.333d);
        double restoreNow = CombatOwnerAction.ResolveRestoreTime(0d, 2000d);
        require(restoreNow == 2000d && reconnect.HasAnimation(restoreNow) && reconnect.RemainingInput(restoreNow) == 0d,
            "Reliable spawn batch time must prevent restarting locks before the first time snapshot.");
        require(!reconnect.HasAnimation(CombatOwnerAction.ResolveRestoreTime(0d, 2001d)),
            "An already completed accepted animation must not replay on a newly initialized client clock.");
        require(CombatOwnerAction.ResolveRestoreTime(2002d, 2000d) == 2002d &&
                CombatOwnerAction.ResolveRestoreTime(2002d, double.NaN) == 2002d,
            "An older or invalid batch timestamp must not rewind a synchronized owner clock.");

        var stacks = new PoisonStackCollection<object, int>();
        var ticks = new List<PoisonTick<object, int>>();
        object first = new object();
        object second = new object();
        require(stacks.Add(first, 1, 10d, 3d, 2), "A normal poison hit must add its target.");
        stacks.Add(second, 2, 10d, 3d, 2);
        stacks.Add(first, 3, 11d, 3d, 2);
        stacks.Add(first, 4, 12d, 3d, 2);
        stacks.CollectTicks(12d, 2f, _ => true, ticks);
        require(ticks.Count == 2 && ReferenceEquals(ticks[0].Target, second) && ReferenceEquals(ticks[1].Target, first),
            "Poison must preserve the existing reverse target damage order.");
        require(ticks[0].Damage == 2f && ticks[1].Damage == 4f && ticks[1].HitPosition == 4,
            "Refresh must cap stacks and retain the latest hit position.");
        stacks.CollectTicks(13d, 2f, _ => true, ticks);
        require(ticks.Count == 1 && ReferenceEquals(ticks[0].Target, first), "Each target expires independently.");
        stacks.CollectTicks(15d, 2f, _ => true, ticks);
        require(ticks.Count == 0 && stacks.Count == 0, "Exact expiry must stop poison damage.");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d, 0d })
            require(!stacks.Add(first, 0, 0d, invalid, 2) && stacks.Count == 0,
                "Invalid poison lifetime must not add a target.");
        require(!stacks.Add(null, 0, 0d, 3d, 2) && !stacks.Add(first, 0, 0d, 3d, 0),
            "Missing target or zero stack cap must reject poison application.");
        stacks.Add(first, 0, 0d, 3d, 2);
        stacks.Add(first, 0, 0d, 3d, 2);
        stacks.CollectTicks(1d, float.MaxValue, _ => true, ticks);
        require(ticks.Count == 1 && ticks[0].Damage == float.MaxValue, "Poison multiplication overflow must remain finite.");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -1f, 0f })
        {
            stacks.CollectTicks(1d, invalid, _ => true, ticks);
            require(ticks.Count == 0 && stacks.Count == 1, "Invalid damage must not emit a tick or discard valid lifetime.");
        }
        stacks.CollectTicks(2d, 2f, _ => false, ticks);
        require(stacks.Count == 0 && ticks.Count == 0, "Dead or removed targets must stop receiving poison.");
        stacks.Add(first, 0, 0d, 3d, 2);
        stacks.Clear();
        stacks.CollectTicks(1d, 2f, _ => true, ticks);
        require(stacks.Count == 0 && ticks.Count == 0, "Owner disposal must clear remaining poison.");
        for (int i = 0; i < 3; i++) stacks.Add(first, 0, 0d, 6d, 3);
        stacks.Add(first, 1, 6d, 6d, 3);
        stacks.CollectTicks(6d, 2f, _ => true, ticks);
        require(ticks.Count == 1 && ticks[0].Damage == 2f && ticks[0].HitPosition == 1,
            "Reapplying at exact expiry before the timer tick must start at one stack.");
        stacks.CollectTicks(12d, 2f, _ => true, ticks);
        require(stacks.Count == 0 && ticks.Count == 0, "The refreshed poison must receive its own full lifetime.");

        require(CombatRequestSequences.RestoreOwner(0u, 42u) == 42u, "A new owner must use the retained server sequence.");
        require(CombatRequestSequences.Next(42u) == 43u && CombatRequestSequences.IsNewer(43u, 42u),
            "The first rebound request must advance the existing stream.");
        require(CombatRequestSequences.RestoreOwner(43u, 42u) == 43u,
            "A delayed rebind callback must not rewind an already submitted request.");
        require(CombatRequestSequences.RestoreOwner(0u, uint.MaxValue) == uint.MaxValue,
            "New owners must restore server sequence values near wrap.");
        require(CombatRequestSequences.Next(uint.MaxValue) == 1u && CombatRequestSequences.IsNewer(1u, uint.MaxValue),
            "Wrapping must skip the reserved zero sequence.");
        require(CombatRequestSequences.RestoreOwner(1u, uint.MaxValue) == 1u && !CombatRequestSequences.IsNewer(42u, 42u),
            "Sequence restore must preserve wrapped in-flight requests and reject duplicates.");
    }
}
