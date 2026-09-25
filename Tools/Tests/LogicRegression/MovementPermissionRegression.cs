using System;
using BattlePvp.Combat;

internal static class MovementPermissionRegression
{
    internal static void Run(Action<bool, string> require)
    {
        var history = new MovementControlHistory();
        require(!history.TrySample(10d, out _), "An empty permission history must not authorize movement.");
        history.Record(10d, new MovementControlState(5f, false, false, false));
        history.Record(10.2d, new MovementControlState(0f, true, true, true));
        require(history.TrySample(10.1d, out MovementControlState state) && state.Speed == 5f && !state.JumpLocked,
            "A late packet from before a cast retains its original movement and jump permission.");
        require(history.TrySample(10.2d, out state) && state.MoveLocked && state.JumpLocked && state.CrouchLocked && state.Speed == 0f,
            "The accepted lock applies at its exact transition time.");
        require(state.JumpLockedSince == 10.2d, "Jump launch validation needs the original lock start.");
        require(Math.Abs(state.DistanceBeforeMoveLock(10.1d, 10.3d) - 0.5f) < 0.00001f,
            "The first post-lock packet keeps the distance traveled during its pre-lock interval.");
        require(state.DistanceBeforeMoveLock(10.3d, 10.4d) == 0f,
            "The next locked packet cannot claim the same pre-lock interval twice.");
        require(state.DistanceBeforeMoveLock(11d, 11.1d) == 0f,
            "Reconnection after the lock cannot replay movement before its known retained pose.");
        require(state.DistanceBeforeMoveLock(double.NaN, 10.4d) == 0f,
            "Invalid timestamps cannot add transition distance.");
        history.Record(10.3d, new MovementControlState(0f, true, true, false));
        require(history.TrySample(10.3d, out state) && state.JumpLockedSince == 10.2d,
            "An unrelated flag change must not restart jump lock grace.");
        require(state.MoveLockedSince == 10.2d && state.SpeedBeforeMoveLock == 5f,
            "An unrelated flag change must preserve the original move transition.");
        history.Record(10.8d, new MovementControlState(3.5f, false, false, false));
        require(history.TrySample(10.79d, out state) && state.MoveLocked,
            "Releasing a lock must not rewrite the permission of older packets.");
        require(history.TrySample(10.8d, out state) && !state.MoveLocked && state.Speed == 3.5f,
            "Crouch speed applies after the lock ends.");
        history.Record(double.NaN, new MovementControlState(100f, false, false, false));
        history.Record(11d, new MovementControlState(float.PositiveInfinity, false, false, false));
        history.Record(9d, new MovementControlState(100f, false, false, false));
        require(history.TrySample(11d, out state) && state.Speed == 3.5f,
            "Invalid or reversed permission updates must preserve the last trusted state.");
        require(!history.TrySample(double.NaN, out _), "Invalid packet time must not resolve permission.");
        for (int i = 0; i < 70; i++) history.Record(12d + i, new MovementControlState(3f + i % 2, false, false, false));
        require(!history.TrySample(10.2d, out _), "History overflow must reject unknown old permission rather than guess.");
        require(history.TrySample(81d, out state) && state.Speed == 4f, "Recent permission remains available after wrap.");
        history.Clear();
        require(!history.TrySample(81d, out _), "Teleport/reset must remove previous permission history.");

        var flight = new MovementFlightState(0f, 10d, true, 0f);
        require(!flight.TryAccept(0f, 0.5f, 0f, 10d, 10.1d, 1.4f, 9.81f, false, 45f,
            true, 10d, out _), "A new jump after a lock must be rejected.");
        require(!flight.TryAccept(0f, 0.5f, 0f, 9.5d, 10.1d, 1.4f, 9.81f, false, 45f,
            true, 10d, out _), "Initial packet grace cannot invent a launch before the known grounded spawn.");
        require(flight.TryAccept(0f, 0.5f, 0f, 10d, 10.1d, 1.4f, 9.81f, false, 45f,
            false, double.NegativeInfinity, out flight), "A normal launch before the lock must be accepted.");
        require(flight.TryAccept(0.5f, 1.2f, 0f, 10.1d, 10.3d, 1.4f, 9.81f, false, 45f,
            true, 10.2d, out flight), "A lock entering during flight must preserve the current jump budget.");
        require(flight.JumpBudget == 1.4f, "In-flight lock must not reinterpret jump height as zero.");
        double originalFlightTime = flight.GroundTime;
        flight = flight.Rebase(1.2f, 10.35d, false);
        require(flight.GroundHeight == 0f && flight.GroundTime == originalFlightTime && flight.JumpBudget == 1.4f,
            "Airborne reconnect preserves ground height, flight start and accepted launch budget.");
        require(flight.TryAccept(1.2f, 1.35f, 0f, 10.3d, 10.4d, 1.4f, 9.81f, false, 45f,
            true, 10.2d, out flight), "The original jump remains valid after reconnect.");
        require(!flight.TryAccept(1.35f, 2.4f, 0f, 10.4d, 10.5d, 1.4f, 9.81f, false, 45f,
            true, 10.2d, out _), "Reconnect cannot grant another jump above the original envelope.");
        flight = flight.Rebase(0f, 11d, true);
        require(!flight.TryAccept(0f, 0.5f, 0f, 11d, 11.1d, 1.4f, 9.81f, false, 45f,
            true, 10.2d, out _), "Landing does not remove an outstanding jump lock.");
        require(flight.TryAccept(0f, 0.5f, 0f, 11d, 11.1d, 1.4f, 9.81f, false, 45f,
            false, double.NegativeInfinity, out _), "Landing and unlocking allow a normal new jump.");

        flight = new MovementFlightState(0f, 20d, true, 0f);
        float lateHeight = (float)Math.Sqrt(2f * 1.4f * 9.81f) * 0.2f - 0.5f * 9.81f * 0.2f * 0.2f;
        require(flight.TryAccept(0f, lateHeight, 0f, 20d, 20.2d, 1.4f, 9.81f, false, 45f,
            true, 20.1d, out _), "A first flight packet delayed across the lock can prove its pre-lock ballistic launch.");
        require(!flight.TryAccept(0f, 0.5f, 0f, 20.1d, 20.2d, 1.4f, 9.81f, false, 45f,
            true, 20.1d, out _), "A last accepted grounded packet at the lock rules out a pre-lock launch.");
        require(flight.TryAccept(0f, -0.1f, 0f, 20d, 20.1d, 1.4f, 9.81f, false, 45f,
            true, 20d, out flight), "Jump lock must still permit falling off a ledge.");
        require(flight.JumpBudget == 0f && !flight.TryAccept(-0.1f, 0.6f, 0f, 20.1d, 20.2d,
            1.4f, 9.81f, false, 45f, false, double.NegativeInfinity, out _),
            "Unlocking after a fall begins must not create an airborne jump.");
        flight = flight.Rebase(0f, 21d, true);
        require(flight.TryAccept(0f, 0.2f, 0.4f, 21d, 21.1d, 1.4f, 9.81f, true, 45f,
            true, 20d, out _), "Grounded slope/step movement is not a jump launch.");

        flight = new MovementFlightState(0f, 0d, true, 0f);
        float priorY = 0f;
        double priorTime = 0d;
        for (int i = 1; i <= 10; i++)
        {
            float t = i * 0.1f;
            float y = (float)Math.Sqrt(2f * 1.4f * 9.81f) * t - 0.5f * 9.81f * t * t;
            require(flight.TryAccept(priorY, y, 0f, priorTime, t, 1.4f, 9.81f, false, 45f,
                i >= 3, 0.2d, out flight), "Original ballistic flight remains accepted through lock, sample " + i);
            priorY = y; priorTime = t;
        }
        require(!flight.TryAccept(priorY, 1f, 0f, priorTime, 2d, 1.4f, 9.81f, false, 45f,
            true, 0.2d, out _), "Maintaining the old launch budget must not permit hovering.");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            require(!flight.TryAccept(priorY, invalid, 0f, priorTime, 1.1d, 1.4f, 9.81f, false, 45f,
                false, 0d, out _), "Invalid coordinates must not enter flight state.");
        require(Math.Abs(MovementReconnectTiming.Remaining(0d, 100d, 100.1d) - 0.1d) < 0.00001d,
            "First reliable reconnect cannot turn a 0.1 second push into 100 seconds.");
        require(MovementReconnectTiming.Remaining(0d, 100d, 99.9d) == 0d,
            "A forced move expired before spawn must not restart.");
        require(Math.Abs(MovementReconnectTiming.Remaining(100.05d, 100d, 100.1d) - 0.05d) < 0.00001d,
            "An already synchronized owner clock must not rewind to the batch time.");
        require(MovementReconnectTiming.Remaining(100d, 100d, double.NaN) == 0d,
            "An invalid forced-move deadline must not create a coroutine.");
    }
}
