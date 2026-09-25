using System;
using BattlePvp.Networking;

internal static class HostRoomLeaseRegression
{
    internal static void Run(Action<bool, string> require)
    {
        CheckDeadlineValidation(require);
        CheckUnconfirmedRegistration(require);
        CheckDelayedResponses(require);
        CheckHeartbeatAndRetry(require);
        CheckInvalidResponsesAndStop(require);
    }

    private static void CheckUnconfirmedRegistration(Action<bool, string> require)
    {
        var unanswered = new HostRoomLease();
        unanswered.BeginRegistration(100d);
        require(!unanswered.HasExpired(159.999d) && unanswered.HasExpired(160d),
            "A registration with no response must still expire after sixty seconds.");
        require(!unanswered.TryBeginHeartbeat(115d) && !unanswered.TryBeginHeartbeat(159d),
            "An unconfirmed host registration must not start heartbeats.");
        unanswered.Failed(120d);
        unanswered.BeginRegistration(125d);
        require(unanswered.HasExpired(160d), "Registration retries must not extend the first request's waiting deadline.");
        require(!unanswered.Accept(125d, 160d, 1000d, 61000d),
            "Even a retry's live lease cannot revive an initial registration that has already timed out.");
        require(!unanswered.TryBeginHeartbeat(161d), "A rejected first acknowledgement must leave registration unconfirmed.");

        var confirmed = new HostRoomLease();
        confirmed.BeginRegistration(100d);
        confirmed.BeginRegistration(110d);
        require(confirmed.Accept(100d, 159.999d, 1000d, 61000d),
            "An initial acknowledgement just before the registration deadline must still succeed.");
        require(confirmed.TryBeginHeartbeat(159.999d) && confirmed.HasExpired(160d),
            "A late first acknowledgement must enable renewal without extending its original lease.");
        foreach (double invalid in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
        {
            var invalidStart = new HostRoomLease();
            invalidStart.BeginRegistration(invalid);
            require(invalidStart.HasExpired(100d) && !invalidStart.TryBeginHeartbeat(100d),
                "An invalid registration clock must not leave an unlimited pending host.");
            require(!invalidStart.Accept(100d, 101d, 1000d, 61000d),
                "Invalid registration timing must not later be restored by an unrelated acknowledgement.");
        }
        var stopped = new HostRoomLease();
        stopped.Stop();
        stopped.BeginRegistration(100d);
        require(!stopped.Accept(100d, 101d, 1000d, 61000d) && !stopped.TryBeginHeartbeat(115d),
            "Starting registration must not reopen a stopped room lease.");
    }

    private static void CheckDeadlineValidation(Action<bool, string> require)
    {
        require(HostRoomLease.LifetimeSeconds == 60d && HostRoomLease.HeartbeatSeconds == 15d &&
            HostRoomLease.RetrySeconds == 5d, "Host leases must use the agreed 60/15/5 second policy.");
        foreach (double remaining in new[] { 0.001d, 1d, 15d, 60d })
        {
            require(HostRoomLease.TryGetDeadline(100d, 1000d, 1000d + remaining * 1000d, out double deadline),
                "A positive remaining lease of at most sixty seconds must be accepted.");
            require(Math.Abs(deadline - (100d + remaining)) < 0.0000001d,
                "The deadline must be based on request start, not response receipt.");
        }
        foreach (double expiry in new[] { -1d, 0d, 999d, 1000d, 61000.001d, double.MaxValue })
            require(!HostRoomLease.TryGetDeadline(100d, 1000d, expiry, out _),
                "Expired, nonpositive, or overlong server leases must be rejected.");
        foreach (double invalid in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
        {
            require(!HostRoomLease.TryGetDeadline(invalid, 1000d, 61000d, out _),
                "A nonfinite request start must not create a deadline.");
            require(!HostRoomLease.TryGetDeadline(100d, invalid, 61000d, out _),
                "A nonfinite server time must not create a deadline.");
            require(!HostRoomLease.TryGetDeadline(100d, 1000d, invalid, out _),
                "A nonfinite server expiry must not create a deadline.");
        }
        require(!HostRoomLease.TryGetDeadline(100d, 0d, 60000d, out _) &&
            !HostRoomLease.TryGetDeadline(100d, -1000d, 59000d, out _),
            "A missing or nonpositive authoritative server timestamp must not advertise a room.");
    }

    private static void CheckDelayedResponses(Action<bool, string> require)
    {
        foreach (double latency in new[] { 0d, 1d, 14.999d, 15d, 59.999d, 60d, 65d })
        {
            var lease = new HostRoomLease();
            bool accepted = lease.Accept(100d, 100d + latency, 1000d, 61000d);
            require(accepted == (latency < 60d), "A response arriving at or after its conservative deadline must be rejected.");
            if (!accepted)
            {
                require(!lease.TryBeginHeartbeat(200d), "A rejected first response must not start a renewable lease.");
                continue;
            }
            require(!lease.HasExpired(159.999d) && lease.HasExpired(160d),
                "Network latency must never extend the initial sixty-second deadline.");
            require(!lease.TryBeginHeartbeat(160d), "A heartbeat must not start at the exact lease expiry.");
        }
        var renewable = new HostRoomLease();
        require(renewable.Accept(100d, 101d, 1000d, 61000d), "A valid initial response must start the host lease.");
        require(renewable.TryBeginHeartbeat(115d), "A live lease must permit a heartbeat at fifteen seconds.");
        require(renewable.Accept(115d, 130d, 16000d, 76000d), "A renewal received before the old deadline must succeed.");
        require(!renewable.HasExpired(174.999d) && renewable.HasExpired(175d),
            "A delayed renewal must expire sixty seconds after its request started, not after its response arrived.");
        require(renewable.TryBeginHeartbeat(130d), "The next heartbeat interval must also use the previous request start.");
        require(!renewable.Accept(130d, 175d, 31000d, 91000d),
            "A renewal arriving after the previously accepted lease expires must not resurrect the host.");
    }

    private static void CheckHeartbeatAndRetry(Action<bool, string> require)
    {
        var lease = new HostRoomLease();
        require(!lease.HasExpired(100d) && !lease.TryBeginHeartbeat(100d),
            "An uninitialized lease must not send heartbeats.");
        require(lease.Accept(100d, 101d, 1000d, 61000d), "Initial registration must accept a live server lease.");
        require(!lease.TryBeginHeartbeat(114.999d) && lease.TryBeginHeartbeat(115d),
            "Heartbeats must remain blocked until the exact fifteen-second boundary.");
        require(!lease.TryBeginHeartbeat(115d) && !lease.TryBeginHeartbeat(120d),
            "An in-flight heartbeat must exclude duplicate requests even after its interval elapses.");
        lease.Failed(116d);
        require(!lease.TryBeginHeartbeat(120.999d) && lease.TryBeginHeartbeat(121d),
            "A failed heartbeat must retry at, and not before, five seconds after failure.");
        require(!lease.TryBeginHeartbeat(122d), "A retry must also remain exclusive while awaiting its response.");
        lease.Failed(159d);
        require(!lease.TryBeginHeartbeat(164d) && lease.HasExpired(160d),
            "A retry delay must never extend the last accepted host lease.");
    }

    private static void CheckInvalidResponsesAndStop(Action<bool, string> require)
    {
        foreach (double invalid in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
        {
            var lease = new HostRoomLease();
            require(lease.Accept(100d, 101d, 1000d, 61000d), "A baseline lease must be valid before invalid-input checks.");
            require(!lease.Accept(invalid, 110d, 1000d, 61000d) &&
                !lease.Accept(100d, invalid, 1000d, 61000d) &&
                !lease.Accept(100d, 110d, invalid, 61000d) &&
                !lease.Accept(100d, 110d, 1000d, invalid),
                "Invalid renewal timestamps must be rejected in every argument position.");
            require(!lease.Accept(110d, 109d, 1000d, 61000d), "A response cannot arrive before its own request starts.");
            require(!lease.HasExpired(159.999d) && lease.HasExpired(160d),
                "Rejected responses must leave the previously accepted expiry intact.");
            require(lease.HasExpired(invalid) && !lease.TryBeginHeartbeat(invalid),
                "An invalid local clock must not authorize a live heartbeat.");
            require(lease.TryBeginHeartbeat(115d), "Invalid responses must not corrupt a previously valid heartbeat schedule.");
            lease.Failed(invalid);
            require(!lease.TryBeginHeartbeat(115d) && !lease.TryBeginHeartbeat(120d),
                "A nonfinite failure time must not bypass the retry delay.");
        }
        foreach (bool initialize in new[] { false, true })
        {
            var lease = new HostRoomLease();
            if (initialize)
            {
                require(lease.Accept(100d, 101d, 1000d, 61000d), "A valid lease must initialize before stop checks.");
                require(lease.TryBeginHeartbeat(115d), "Stop must also cancel a pending heartbeat.");
            }
            lease.Stop();
            lease.Stop();
            lease.Failed(116d);
            require(!lease.Accept(115d, 117d, 16000d, 76000d),
                "A delayed success must not restore a stopped room lease.");
            require(!lease.TryBeginHeartbeat(121d) && !lease.TryBeginHeartbeat(200d),
                "Failure callbacks and repeated stop calls must not revive heartbeat scheduling.");
        }
    }
}
