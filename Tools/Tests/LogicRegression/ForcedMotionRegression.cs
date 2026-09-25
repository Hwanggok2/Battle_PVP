using System;
using BattlePvp.Combat;

internal static class ForcedMotionRegression
{
    internal static void Run(Action<bool, string> require)
    {
        var motion = new ServerForcedMotion();
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            require(!motion.TryBegin(invalid, 0f, 1.5f, 0.2f, 10d), "Nonfinite forced direction is rejected.");
            require(!motion.TryBegin(1f, 0f, invalid, 0.2f, 10d), "Nonfinite forced distance is rejected.");
            require(!motion.TryBegin(1f, 0f, 1.5f, invalid, 10d), "Nonfinite forced duration is rejected.");
        }
        require(!motion.TryBegin(0f, 0f, 1.5f, 0.2f, 10d), "A zero direction must be resolved by the server before starting.");
        require(!motion.TryBegin(1f, 0f, 0f, 0.2f, 10d), "Zero forced distance does not acquire movement authority.");
        require(!motion.TryBegin(1f, 0f, 1.5f, -1f, 10d), "Negative duration is rejected.");
        require(!motion.TryBegin(1f, 0f, 1.5f, 0.2f, double.NaN), "Invalid server clock is rejected.");
        require(motion.TryBegin(2f, 0f, 1.5f, 0.2f, 10d) && motion.DirectionX == 1f && motion.Speed == 7.5f,
            "The normal kick keeps its configured direction, distance and duration.");
        require(!motion.AcceptsOwnerPose(7u, 7u) && !motion.AcceptsOwnerPose(6u, 7u),
            "Ignoring the RPC, standing still or submitting a reverse pose cannot enter the owner pose path while forced.");
        double distance = motion.TakeStep(10.05d) * motion.Speed;
        require(Math.Abs(distance - 0.375d) < 0.00001d, "Server clock advances the kick without any client input.");
        require(motion.TakeStep(10.05d) == 0d && motion.TakeStep(9d) == 0d && motion.TakeStep(double.NaN) == 0d,
            "Repeated, reversed and invalid ticks never duplicate displacement.");
        distance += motion.TakeStep(11d) * motion.Speed;
        require(Math.Abs(distance - 1.5d) < 0.00001d && motion.HasFinished,
            "A host hitch consumes the full remaining short kick once.");
        require(!motion.AcceptsOwnerPose(7u, 7u), "A completed clock still holds authority until the reliable final-pose handoff.");
        require(motion.TakeStep(12d) == 0d, "Wall-blocked displacement is not retried beyond the effect deadline.");
        motion.Cancel();
        require(motion.AcceptsOwnerPose(8u, 8u) && !motion.AcceptsOwnerPose(7u, 8u),
            "Only the new epoch may resume owner movement after the final server pose.");

        motion.TryBegin(0f, 3f, 3f, 0.35f, 20d);
        require(motion.TrySetOwnerInput(1.02f, 0f, 1u, 20.01d, 20.02d) && motion.InputX == 1f,
            "Normal directional input is bounded independently of the mandatory displacement.");
        require(!motion.TrySetOwnerInput(2f, 0f, 2u, 20.03d, 20.03d), "Oversized input cannot reverse or amplify the force.");
        require(!motion.TrySetOwnerInput(float.NaN, 0f, 2u, 20.03d, 20.03d), "Invalid input is rejected.");
        require(!motion.TrySetOwnerInput(0f, 1f, 2u, 20.01d, 20.03d), "Duplicate input time cannot create another jump.");
        require(!motion.TrySetOwnerInput(0f, 1f, 2u, 21d, 20.03d), "Future input is rejected.");
        require(!motion.TrySetOwnerInput(0f, 1f, 2u, 19d, 20.03d), "Old input is rejected.");
        require(motion.HasInput(20.1d) && !motion.HasInput(20.3d), "Missing owner input expires instead of steering forever.");
        require(!motion.TryConsumeJump(20.04d, false) && motion.TryConsumeJump(20.05d, true),
            "Jump buffering requires the server's current ground and input-lock decision.");
        require(!motion.TryConsumeJump(20.06d, true), "One accepted jump sequence is consumed once.");
        require(motion.TrySetOwnerInput(0f, 1f, 1u, 20.07d, 20.07d) && !motion.TryConsumeJump(20.08d, true),
            "Repeating the last jump sequence does not jump again.");
        require(motion.TrySetOwnerInput(0f, 1f, 2u, 20.09d, 20.09d) && motion.TryConsumeJump(20.1d, true),
            "A genuinely new permitted jump still works.");
        double deadline = motion.EndsAt;
        motion.ResetOwnerInput();
        require(motion.IsActive && motion.EndsAt == deadline && !motion.HasInput(20.1d),
            "Disconnect/reconnect clears input but preserves the server force deadline.");
        require(motion.TrySetOwnerInput(0f, 0f, 0u, 20.11d, 20.11d) && !motion.TryConsumeJump(20.12d, true),
            "The new owner starts with sequence zero and cannot replay the previous owner's jump.");
        require(Math.Abs(motion.TakeStep(21d) * motion.Speed - 3d) < 0.00001d,
            "Reconnect and a missing first client time snapshot cannot restart the roll clock.");

        motion.TryBegin(1f, 0f, 2f, 0.2f, 30d);
        double firstSegment = motion.TakeStep(30.1d) * motion.Speed;
        require(!motion.TryBegin(1f, 0f, float.NaN, 0.2f, 30.1d) && motion.EndsAt > 30.19d,
            "Invalid replacement preserves the already accepted force.");
        require(motion.TryBegin(-1f, 0f, 0.5f, 0.1f, 30.1d), "A later valid effect replaces the earlier one, as before.");
        double total = firstSegment + motion.TakeStep(31d) * motion.Speed * motion.DirectionX;
        require(Math.Abs(total - 0.5d) < 0.00001d,
            "Replacement keeps the old consumed interval and only the new remaining force.");
        motion.Cancel();
        require(!motion.IsActive && motion.TakeStep(32d) == 0d && !motion.HasInput(32d),
            "Death, teleport and disable cancellation leave no pending force or input.");
    }
}
