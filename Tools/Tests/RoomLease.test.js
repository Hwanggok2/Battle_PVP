const assert = require('node:assert/strict');
const { load, HOST, GUEST, REGISTRY, ROOM, clone } = require('./NetworkProfileRoomCapacity.test.js');

const LEASE = 'ROOMLEASE_' + ROOM, CLOSED = 'ROOMCLOSED_' + ROOM;
const ARGS = { roomId: ROOM, roomName: 'Lease regression', masterName: 'Host', relayJoinCode: 'RELAY1' };
const CHALLENGE = '1234567890abcdef1234567890abcdef';
let assertions = 0, scenarios = 0;
const equal = (actual, expected, message) => { assertions++; assert.equal(actual, expected, message); };
const ok = (condition, message) => { assertions++; assert.ok(condition, message); };
const rejects = (operation, message) => { assertions++; assert.throws(operation, undefined, message); };
const state = fixture => JSON.stringify([...fixture.groups]);
const as = (fixture, account, operation) => {
    const previous = fixture.context.currentPlayerId;
    fixture.context.currentPlayerId = account;
    try { return operation(fixture.context.handlers); }
    finally { fixture.context.currentPlayerId = previous; }
};

function setup(file, guest = true) {
    const fixture = load(file);
    fixture.context.handlers.RegisterRoomToRegistry(ARGS);
    if (guest) as(fixture, GUEST, h => h.JoinRoom(ARGS));
    return fixture;
}

function hiddenAndRejected(fixture, message) {
    const h = fixture.context.handlers;
    equal(h.GetActiveRoomInfos({}).roomInfos[ROOM], undefined, message + ': detailed listing');
    equal(h.GetActiveRooms({}).rooms[ROOM], undefined, message + ': name listing');
    const before = state(fixture), adds = fixture.calls.add;
    rejects(() => as(fixture, GUEST, api => api.JoinRoom(ARGS)), message + ': join');
    rejects(() => as(fixture, GUEST, api => api.ApproveRoomConnection({ ...ARGS, challenge: CHALLENGE })), message + ': approval');
    rejects(() => as(fixture, HOST, api => api.VerifyRoomConnection({ ...ARGS, challenge: CHALLENGE, playerId: GUEST })), message + ': verification');
    rejects(() => as(fixture, HOST, api => api.UpdateRoomRelayJoinCode({ ...ARGS, relayJoinCode: 'LATE' })), message + ': relay update');
    rejects(() => as(fixture, HOST, api => api.HeartbeatRoom(ARGS)), message + ': late heartbeat');
    rejects(() => as(fixture, HOST, api => api.RegisterRoomToRegistry(ARGS)), message + ': same-id registration');
    equal(fixture.calls.add, adds, message + ': rejected admission cannot add members');
    equal(state(fixture), before, message + ': rejection must not revive metadata or renew the lease');
}

for (const file of ['roomRegistry.js', 'combinedCloudScript.js']) {
    const run = (name, body) => {
        try { body(); scenarios++; }
        catch (error) { throw new Error(file + ' / ' + name + ': ' + (error.stack || error)); }
    };

    run('server-clock lease and normal heartbeat', () => {
        const f = setup(file), h = f.context.handlers, startedAt = f.options.now;
        equal(Number(f.group(REGISTRY).data[LEASE].Value), startedAt + 60000, 'Initial lease must last exactly 60 seconds.');
        let listed = h.GetActiveRoomInfos({}).roomInfos[ROOM];
        equal(listed.leaseExpiresAt, startedAt + 60000);
        equal(listed.serverNow, startedAt);
        for (let heartbeat = 1; heartbeat <= 8; heartbeat++) {
            f.options.now = startedAt + heartbeat * 15000;
            const result = h.HeartbeatRoom({ ...ARGS, leaseExpiresAt: Number.MAX_SAFE_INTEGER, serverNow: 0 });
            equal(result.ok, true);
            equal(result.serverNow, f.options.now);
            equal(result.leaseExpiresAt, f.options.now + 60000, 'The authenticated owner cannot choose the lease duration.');
            equal(Number(f.group(REGISTRY).data[LEASE].Value), result.leaseExpiresAt);
            listed = h.GetActiveRoomInfos({}).roomInfos[ROOM];
            equal(listed.leaseExpiresAt, result.leaseExpiresAt, 'Regular heartbeats must keep the room visible beyond its initial expiry.');
        }
        const before = state(f);
        h.GetActiveRooms({}); h.GetActiveRoomInfos({});
        equal(state(f), before, 'Listing must not mutate or renew a lease.');
    });

    run('registration and guest activity do not renew leases', () => {
        const f = setup(file), h = f.context.handlers;
        const initialExpiry = f.group(REGISTRY).data[LEASE].Value;
        f.options.now += 15000;
        h.RegisterRoomToRegistry(ARGS);
        h.UpdateRoomRelayJoinCode({ ...ARGS, relayJoinCode: 'RELAY2' });
        as(f, GUEST, api => api.JoinRoom(ARGS));
        as(f, GUEST, api => api.ApproveRoomConnection({ ...ARGS, challenge: CHALLENGE }));
        equal(f.group(REGISTRY).data[LEASE].Value, initialExpiry, 'Only the owner heartbeat may renew an existing lease.');
        const before = state(f);
        rejects(() => as(f, GUEST, api => api.HeartbeatRoom({ ...ARGS, ownerId: HOST, isHost: true })), 'Guest metadata cannot authorize a heartbeat.');
        equal(state(f), before);
        equal(as(f, GUEST, api => api.LeaveRoom({ ...ARGS, ownerId: HOST, isHost: true })).playerCount, 1);
        equal(f.group(REGISTRY).data[CLOSED], undefined, 'A guest leaving must not close the host room.');
        equal(f.group(REGISTRY).data[LEASE].Value, initialExpiry);
        ok(h.GetActiveRoomInfos({}).roomInfos[ROOM], 'Host membership and room visibility must survive guest departure.');
    });

    run('host crash expires admission and authentication without LeaveRoom', () => {
        const f = setup(file), h = f.context.handlers, startedAt = f.options.now;
        f.options.now = startedAt + 59000;
        as(f, GUEST, api => api.ApproveRoomConnection({ ...ARGS, challenge: CHALLENGE }));
        f.options.now = startedAt + 59999;
        ok(h.GetActiveRoomInfos({}).roomInfos[ROOM], 'A lease is live one millisecond before expiry.');
        equal(h.VerifyRoomConnection({ ...ARGS, challenge: CHALLENGE, playerId: GUEST }).ok, true);
        const before = state(f);
        f.options.now = startedAt + 60000;
        hiddenAndRejected(f, 'Lease expiry boundary');
        equal(state(f), before, 'A crashed host room is hidden without destructive listing cleanup.');
        equal(f.group(ROOM).members.length, 2, 'This scenario must not rely on membership removal or a leave callback.');
        f.options.now++;
        hiddenAndRejected(f, 'After lease expiry');
    });

    run('missing and malformed leases cannot be supplied through metadata', () => {
        for (const invalid of [null, '', 'NaN', 'Infinity', '-1', '0']) {
            const f = setup(file);
            if (invalid === null) delete f.group(REGISTRY).data[LEASE];
            else f.group(REGISTRY).data[LEASE] = { Value: invalid };
            const metadata = JSON.parse(f.group(REGISTRY).data[ROOM].Value);
            metadata.leaseExpiresAt = f.options.now + 60000;
            metadata.serverNow = f.options.now;
            f.group(REGISTRY).data[ROOM].Value = JSON.stringify(metadata);
            f.group(ROOM).data.LeaseExpiresAt = { Value: String(f.options.now + 60000) };
            hiddenAndRejected(f, 'Invalid authoritative lease ' + invalid);
        }
    });

    run('lease expiry cannot exceed the server lifetime bound', () => {
        const f = setup(file);
        for (const expiry of [f.options.now + 60001, 999999999999999]) {
            f.group(REGISTRY).data[LEASE] = { Value: String(expiry) };
            hiddenAndRejected(f, 'Lease beyond the 60-second server bound ' + expiry);
        }
    });

    run('outsider leave during initial registration cannot close the pending owner room', () => {
        const f = load(file);
        let interleaved = false;
        f.options.afterWrite = request => {
            if (request.SharedGroupId !== REGISTRY || !request.Data || request.Data[LEASE] === undefined) return;
            f.options.afterWrite = null;
            interleaved = true;
            equal(f.groups.has(ROOM), false, 'The outsider request must run after the lease write but before room-group creation.');
            equal(Number(f.group(REGISTRY).data[LEASE].Value), f.options.now + 60000);
            const before = state(f), writes = f.calls.write, removes = f.calls.remove;
            const result = as(f, GUEST, h => h.LeaveRoom({ ...ARGS, ownerId: HOST, isHost: true }));
            equal(result.playerCount, 0, 'An outsider has no membership to remove from the not-yet-created group.');
            equal(state(f), before, 'The outsider must not change the pending lease, metadata, or closed marker.');
            equal(f.calls.write, writes, 'Missing room-group lookup must not authorize outsider registry writes.');
            equal(f.calls.remove, removes, 'Missing room-group lookup must not attempt a membership mutation.');
        };
        const registered = f.context.handlers.RegisterRoomToRegistry(ARGS);
        ok(interleaved, 'The regression must actually interleave an outsider leave with production registration.');
        equal(registered.ok, true, 'The original owner registration must complete after the outsider request.');
        equal(f.group(REGISTRY).data[CLOSED], undefined);
        equal(f.group(ROOM).members.length, 1);
        ok(f.context.handlers.GetActiveRoomInfos({}).roomInfos[ROOM], 'The completed owner room must remain live and discoverable.');
    });

    run('outsider leave cannot close registry metadata whose group is temporarily missing', () => {
        const f = setup(file, false);
        f.groups.delete(ROOM); // Model an incomplete service sequence: registry metadata/lease are already present.
        const before = state(f), writes = f.calls.write;
        equal(as(f, GUEST, h => h.LeaveRoom(ARGS)).playerCount, 0);
        equal(state(f), before, 'A nonmember must not delete metadata or tombstone a different owner room.');
        equal(f.calls.write, writes);
        equal(f.context.handlers.RegisterRoomToRegistry(ARGS).ok, true, 'The owner must be able to finish the still-live registration.');
        ok(f.context.handlers.GetActiveRoomInfos({}).roomInfos[ROOM]);
        equal(f.group(REGISTRY).data[CLOSED], undefined);

        f.groups.delete(ROOM);
        f.context.handlers.LeaveRoom(ARGS);
        equal(f.group(REGISTRY).data[CLOSED].Value, 'true', 'The actual owner still has authority to close a pending room without its group.');
        hiddenAndRejected(f, 'Owner explicitly closed a pending registration');
    });

    run('owner departure closes immediately despite remaining guests', () => {
        const f = setup(file), h = f.context.handlers;
        h.LeaveRoom(ARGS);
        equal(f.group(REGISTRY).data[CLOSED].Value, 'true');
        equal(f.group(REGISTRY).data[ROOM], undefined);
        equal(f.group(ROOM).members.length, 1, 'A remaining guest must not keep the ownerless room open.');
        hiddenAndRejected(f, 'Owner left with a guest remaining');
        as(f, GUEST, api => api.LeaveRoom(ARGS));
        equal(f.groups.has(ROOM), false, 'The final guest may still clean up their actual membership.');
        equal(f.group(REGISTRY).data[CLOSED].Value, 'true', 'Room-group deletion must not erase the registry tombstone.');
        const before = state(f);
        as(f, HOST, api => api.LeaveRoom(ARGS));
        as(f, GUEST, api => api.LeaveRoom(ARGS));
        equal(state(f), before, 'Repeated leaves must not recreate a closed room.');
        hiddenAndRejected(f, 'Closed room after its last member leaves');
    });

    run('owner removal failure cannot leave an advertised room', () => {
        const f = setup(file);
        f.options.removeFailure = true;
        try { f.context.handlers.LeaveRoom(ARGS); } catch (_) { /* A failed service removal may propagate. */ }
        equal(f.group(REGISTRY).data[CLOSED].Value, 'true', 'Close must commit before removing the owner membership.');
        equal(f.group(ROOM).members.length, 2, 'The injected service failure must leave the actual members present.');
        hiddenAndRejected(f, 'Owner removal failed after closing');
        f.options.removeFailure = false;
        as(f, HOST, api => api.LeaveRoom(ARGS));
        as(f, GUEST, api => api.LeaveRoom(ARGS));
        equal(f.groups.has(ROOM), false, 'Retrying membership cleanup must still work after an owner-close failure.');
    });

    run('heartbeat commit arriving after owner close cannot reopen the room', () => {
        const f = setup(file);
        f.options.now += 15000;
        let interleaved = false;
        f.options.beforeWrite = request => {
            if (request.SharedGroupId !== REGISTRY || !request.Data || request.Data[LEASE] === undefined) return;
            f.options.beforeWrite = null;
            interleaved = true;
            as(f, HOST, h => h.LeaveRoom(ARGS));
        };
        try { f.context.handlers.HeartbeatRoom(ARGS); } catch (_) { /* A final recheck may detect closure. */ }
        ok(interleaved, 'The close must actually occur between heartbeat validation and its service write.');
        equal(f.group(REGISTRY).data[CLOSED].Value, 'true');
        hiddenAndRejected(f, 'Late heartbeat commit');
    });

    run('late registration metadata commit cannot clear a close tombstone', () => {
        const f = setup(file);
        let interleaved = false;
        f.options.beforeWrite = request => {
            if (request.SharedGroupId !== REGISTRY || !request.Data || request.Data[ROOM] === undefined) return;
            f.options.beforeWrite = null;
            interleaved = true;
            as(f, HOST, h => h.LeaveRoom(ARGS));
        };
        try { f.context.handlers.RegisterRoomToRegistry(ARGS); } catch (_) { /* A final recheck may detect closure. */ }
        ok(interleaved, 'The close must occur before a pending registration metadata write completes.');
        equal(f.group(REGISTRY).data[CLOSED].Value, 'true');
        hiddenAndRejected(f, 'Late registration commit');
    });

    run('replaying stale room and heartbeat writes cannot revive a closed room', () => {
        const f = setup(file);
        const staleMetadata = clone(f.group(REGISTRY).data[ROOM]);
        f.context.handlers.LeaveRoom(ARGS);
        // Simulate delayed shared-group writes completing in reverse response order.
        f.context.server.UpdateSharedGroupData({ SharedGroupId: REGISTRY, Data: {
            [ROOM]: staleMetadata.Value, [LEASE]: String(f.options.now + 60000)
        } });
        equal(f.group(REGISTRY).data[CLOSED].Value, 'true');
        hiddenAndRejected(f, 'Reordered stale service writes');
    });

    run('admin cleanup preserves closed-room protection', () => {
        const f = setup(file);
        f.options.internalKey = 'internal-test-key';
        f.options.now += 60000;
        equal(f.context.handlers.AdminDeleteRoom({ roomId: ROOM, adminKey: 'internal-test-key' }).removedCount, 1,
            'An explicit administrator must be able to remove an expired room.');
        equal(f.group(REGISTRY).data[ROOM], undefined);
        equal(f.group(REGISTRY).data[CLOSED].Value, 'true');
        hiddenAndRejected(f, 'Admin-deleted expired room');

        const fresh = setup(file);
        fresh.options.internalKey = 'internal-test-key';
        fresh.context.handlers.AdminClearRoomRegistry({ adminKey: 'internal-test-key' });
        equal(fresh.group(REGISTRY).data[ROOM], undefined);
        equal(fresh.group(REGISTRY).data[CLOSED].Value, 'true');
        hiddenAndRejected(fresh, 'Admin-cleared live room');
    });
}

console.log(`PASS: ${assertions} room lease assertions across ${scenarios} scenarios against both production CloudScript files.`);
console.log('Local fake-clock/service regression only; no external PlayFab/Relay calls or atomic backend concurrency guarantee.');
