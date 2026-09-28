const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const HOST = 'ABC123', GUEST = 'DEF456', REGISTRY = 'GLOBALROOMREGISTRY';
const ROOM = 'battle_abc123_' + '1'.repeat(32);
const ALLOWED = ['RegisterRoomToRegistry', 'UpdateRoomRelayJoinCode', 'HeartbeatRoom', 'GetActiveRooms', 'GetActiveRoomInfos',
    'JoinRoom', 'LeaveRoom', 'AdminValidateRoomKey', 'AdminDeleteRoom', 'AdminClearRoomRegistry',
    'ApproveRoomConnection', 'VerifyRoomConnection', 'UpdateRoomSettings', 'KickRoomPlayer'];
const clone = value => JSON.parse(JSON.stringify(value));
const sameId = (a, b) => a.toLowerCase() === b.toLowerCase();

// Exported production handlers run unchanged against this local service model.
function load(file) {
    const groups = new Map(), internalData = new Map(), calls = { add: 0, remove: 0, write: 0, publicRead: 0, sampleWrite: 0 };
    const options = { internalKey: null, readFailure: false, removeFailure: false, afterAdd: null,
        readErrorAfterRemove: null, nextMemberReadError: null, now: 1000000, proofReadFailure: false, proofWriteFailure: false,
        beforeWrite: null, afterWrite: null };
    const group = id => {
        if (!groups.has(id)) throw { apiErrorInfo: {
            api: '/Server/GetSharedGroupData',
            apiError: { error: 'InvalidSharedGroupId', errorCode: 1088 }
        } };
        return groups.get(id);
    };
    const forbidden = () => { calls.sampleWrite++; throw new Error('Unexpected sample write'); };
    const context = { handlers: {}, currentPlayerId: HOST, log: { info() {}, debug() {}, error() {} },
        Date: class extends Date { static now() { return options.now; } },
        entity: { SetObjects: forbidden }, http: { request: forbidden }, server: {
            CreateSharedGroup({ SharedGroupId: id }) {
                // The live CreateSharedGroup API reports an ID collision as 1088, not SharedGroupAlreadyExists.
                if (groups.has(id)) throw { message: 'PlayFab API request error',
                    apiErrorInfo: { api: '/Server/CreateSharedGroup',
                        apiError: { error: 'InvalidSharedGroupId', errorCode: 1088 } } };
                groups.set(id, { members: [], data: {} });
            },
            GetSharedGroupData({ SharedGroupId: id, GetMembers }) {
                if (GetMembers && options.nextMemberReadError) {
                    const error = options.nextMemberReadError;
                    options.nextMemberReadError = null;
                    throw error;
                }
                return { Data: clone(group(id).data), Members: GetMembers ? group(id).members.slice() : undefined };
            },
            AddSharedGroupMembers({ SharedGroupId: id, PlayFabIds }) {
                calls.add++;
                for (const player of PlayFabIds)
                    if (!group(id).members.some(member => sameId(member, player))) group(id).members.push(player);
                if (options.afterAdd) options.afterAdd(group(id));
            },
            RemoveSharedGroupMembers({ SharedGroupId: id, PlayFabIds }) {
                calls.remove++;
                if (options.removeFailure) throw new Error('Injected removal failure');
                group(id).members = group(id).members.filter(player => !PlayFabIds.some(removed => sameId(player, removed)));
                if (group(id).members.length === 0) groups.delete(id); // Real API deletes the group and its data.
                options.nextMemberReadError = options.readErrorAfterRemove;
            },
            UpdateSharedGroupData({ SharedGroupId: id, Data, KeysToRemove }) {
                calls.write++;
                if (options.beforeWrite) options.beforeWrite({ SharedGroupId: id, Data, KeysToRemove });
                for (const [key, value] of Object.entries(Data || {})) group(id).data[key] = { Value: value };
                for (const key of KeysToRemove || []) delete group(id).data[key];
                if (options.afterWrite) options.afterWrite({ SharedGroupId: id, Data, KeysToRemove });
            },
            GetTitleInternalData() {
                if (options.readFailure) throw new Error('Injected title read failure');
                return { Data: options.internalKey ? { RoomAdminKey: options.internalKey } : {} };
            },
            GetTitleData() { calls.publicRead++; return { Data: { RoomAdminKey: 'public-test-key' } }; },
            UpdatePlayerStatistics: forbidden,
            UpdateUserInternalData({ PlayFabId, Data }) {
                if (Object.keys(Data).some(key => key !== 'BattleRoomConnectionProof' && !/^Room(Settings|Kick)_battle_/.test(key))) return forbidden();
                if (options.proofWriteFailure) throw new Error('Injected proof write failure');
                internalData.set(PlayFabId.toLowerCase(), { ...(internalData.get(PlayFabId.toLowerCase()) || {}), ...clone(Data) });
            },
            GetUserInternalData({ PlayFabId, Keys }) {
                if (Keys.length !== 1 || (Keys[0] !== 'BattleRoomConnectionProof' && !/^Room(Settings|Kick)_battle_/.test(Keys[0]))) return forbidden();
                if (options.proofReadFailure) throw new Error('Injected proof read failure');
                const source = internalData.get(PlayFabId.toLowerCase()) || {}, Data = {};
                for (const key of Keys) if (source[key] !== undefined) Data[key] = { Value: source[key] };
                return { Data };
            }
        } };
    vm.createContext(context);
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../../Assets/PlayFabCloudScript', file), 'utf8'), context);
    return { context, groups, group, calls, options, internalData };
}

if (require.main === module) for (const file of ['roomRegistry.js', 'combinedCloudScript.js']) {
    const { context: c, groups, group, calls, options, internalData } = load(file), h = c.handlers;
    const args = { roomId: ROOM, roomName: 'Test room', masterName: 'Host', relayJoinCode: 'RELAY1' };
    const state = () => JSON.stringify([...groups]);
    assert.deepEqual(Array.from(c.CLIENT_CLOUDSCRIPT_HANDLER_ALLOWLIST).sort(), ALLOWED.slice().sort());
    assert.equal(h.RegisterRoomToRegistry(args).roomInfo.playerCount, 1);
    assert.equal(group(ROOM).data.RelayJoinCode.Value, 'RELAY1');
    assert.deepEqual(group(REGISTRY).members, []);
    c.currentPlayerId = GUEST;
    assert.equal(h.JoinRoom(args).roomInfo.playerCount, 2);
    const joinedAdds = calls.add;
    c.currentPlayerId = GUEST.toLowerCase();
    assert.equal(h.JoinRoom(args).roomInfo.playerCount, 2);
    assert.equal(calls.add, joinedAdds);
    const challenge = '1234567890abcdef1234567890abcdef';
    const auth = { roomId: ROOM, challenge, playerId: GUEST };
    assert.equal(h.ApproveRoomConnection({ ...auth, playerId: HOST }).playerId, GUEST.toLowerCase(),
        'Approval identity comes from currentPlayerId, never the claimed participant field');
    assert.equal(internalData.has(HOST.toLowerCase()), false);
    assert.throws(() => h.VerifyRoomConnection(auth), /room owner/);
    c.currentPlayerId = HOST;
    assert.equal(h.VerifyRoomConnection(auth).playerId, GUEST.toLowerCase());
    const proofBefore = internalData.get(GUEST.toLowerCase()).BattleRoomConnectionProof;
    assert.equal(h.VerifyRoomConnection(auth).ok, true);
    assert.equal(internalData.get(GUEST.toLowerCase()).BattleRoomConnectionProof, proofBefore,
        'Backend verification must not claim atomic consume or delete a newer approval');
    assert.throws(() => h.VerifyRoomConnection({ ...auth, playerId: HOST }), /approval/);
    assert.throws(() => h.VerifyRoomConnection({ ...auth, playerId: 'a'.repeat(65) }), /valid participant/);
    assert.throws(() => h.VerifyRoomConnection({ ...auth, challenge: '2'.repeat(32) }), /approval/);
    options.now += 30000;
    h.HeartbeatRoom(args);
    options.now += 30000;
    assert.throws(() => h.VerifyRoomConnection(auth), /expired/);
    c.currentPlayerId = GUEST;
    options.proofWriteFailure = true;
    assert.throws(() => h.ApproveRoomConnection(auth), /proof write failure/);
    options.proofWriteFailure = false;
    for (const invalid of ['0'.repeat(32), challenge.toUpperCase(), '__proto__', 'a'.repeat(33)])
        assert.throws(() => h.ApproveRoomConnection({ ...auth, challenge: invalid }), /fresh connection challenge/);
    assert.throws(() => h.ApproveRoomConnection({ ...auth, roomId: REGISTRY }), /Invalid or legacy/);
    h.ApproveRoomConnection({ ...auth, challenge: '3'.repeat(32) });
    c.currentPlayerId = HOST;
    assert.throws(() => h.VerifyRoomConnection(auth), /approval/);
    assert.equal(h.VerifyRoomConnection({ ...auth, challenge: '3'.repeat(32) }).ok, true);
    options.proofReadFailure = true;
    assert.throws(() => h.VerifyRoomConnection(auth), /proof read failure/);
    options.proofReadFailure = false;
    const otherRoom = 'battle_abc123_' + '4'.repeat(32);
    groups.set(otherRoom, { members: [HOST, GUEST], data: {} });
    group(REGISTRY).data[otherRoom] = { Value: JSON.stringify({ roomName: 'Other room', playerCount: 2 }) };
    group(REGISTRY).data['ROOMLEASE_' + otherRoom] = { Value: String(options.now + 60000) };
    assert.throws(() => h.VerifyRoomConnection({ ...auth, roomId: otherRoom, challenge: '3'.repeat(32) }), /approval/);
    groups.delete(otherRoom);
    delete group(REGISTRY).data[otherRoom];
    delete group(REGISTRY).data['ROOMLEASE_' + otherRoom];
    const validProof = internalData.get(GUEST.toLowerCase()).BattleRoomConnectionProof;
    internalData.get(GUEST.toLowerCase()).BattleRoomConnectionProof = '{broken';
    assert.throws(() => h.VerifyRoomConnection({ ...auth, challenge: '3'.repeat(32) }), /approval is invalid/);
    const invalidExpiry = JSON.parse(validProof);
    invalidExpiry.expiresAt = options.now + 60001;
    internalData.get(GUEST.toLowerCase()).BattleRoomConnectionProof = JSON.stringify(invalidExpiry);
    assert.throws(() => h.VerifyRoomConnection({ ...auth, challenge: '3'.repeat(32) }), /approval/);
    internalData.get(GUEST.toLowerCase()).BattleRoomConnectionProof = validProof;
    group(ROOM).members = [HOST];
    assert.throws(() => h.VerifyRoomConnection({ ...auth, challenge: '3'.repeat(32) }), /must have joined/);
    group(ROOM).members = [GUEST];
    assert.throws(() => h.VerifyRoomConnection({ ...auth, challenge: '3'.repeat(32) }), /must have joined/);
    group(ROOM).members = [HOST, GUEST];
    c.currentPlayerId = 'FACE';
    assert.throws(() => h.ApproveRoomConnection(auth), /must have joined/);
    c.currentPlayerId = undefined;
    assert.throws(() => h.ApproveRoomConnection(auth), /authenticated PlayFab player/);
    c.currentPlayerId = GUEST;
    let before = state();
    assert.throws(() => h.RegisterRoomToRegistry({ ...args, ownerId: HOST, isHost: true }), /room owner/);
    assert.throws(() => h.UpdateRoomRelayJoinCode(args), /room owner/);
    assert.equal(state(), before, 'Client role metadata and membership cannot confer ownership');

    for (const roomId of [REGISTRY, '2'.repeat(32), 'legacy-guid-room', ROOM.toUpperCase(), ROOM + 'x',
        'battle_abc123_abc', 'battle_abc123_' + 'A'.repeat(32), '__proto__']) {
        before = state();
        for (const name of ['RegisterRoomToRegistry', 'UpdateRoomRelayJoinCode', 'HeartbeatRoom', 'JoinRoom', 'LeaveRoom'])
            assert.throws(() => h[name]({ ...args, roomId }), /Invalid or legacy room ID/);
        assert.equal(state(), before, 'Invalid/reserved/legacy IDs must have no mutation side effects');
    }
    c.currentPlayerId = undefined;
    assert.throws(() => h.JoinRoom(args), /authenticated PlayFab player/);
    c.currentPlayerId = HOST.toLowerCase();
    group(ROOM).data.PlayerCount.Value = '1'; // Stale metadata must not defeat actual membership.
    assert.equal(h.RegisterRoomToRegistry(args).roomInfo.playerCount, 2);
    assert.equal(group(ROOM).data.PlayerCount.Value, '2');
    assert.equal(calls.add, joinedAdds);
    assert.equal(h.UpdateRoomRelayJoinCode({ ...args, relayJoinCode: 'RELAY2' }).roomInfo.relayJoinCode, 'RELAY2');

    c.currentPlayerId = GUEST;
    assert.equal(h.LeaveRoom(args).playerCount, 1);
    before = state();
    const removed = calls.remove, written = calls.write;
    assert.equal(h.LeaveRoom(args).playerCount, 1);
    c.currentPlayerId = 'FACE';
    assert.equal(h.LeaveRoom(args).playerCount, 1);
    assert.equal(state(), before);
    assert.equal(calls.remove, removed);
    assert.equal(calls.write, written);
    group(ROOM).members.push(GUEST);
    c.currentPlayerId = GUEST;
    before = state();
    options.removeFailure = true;
    assert.throws(() => h.LeaveRoom(args), /Injected removal failure/);
    assert.equal(state(), before);
    options.removeFailure = false;

    group(ROOM).members = [HOST, ...Array.from({ length: 7 }, (_, i) => 'B' + i)];
    c.currentPlayerId = GUEST;
    const fullAdds = calls.add;
    assert.throws(() => h.JoinRoom(args), /Room is full/);
    assert.equal(group(ROOM).members.length, 8);
    assert.equal(calls.add, fullAdds);
    group(ROOM).members = [HOST, ...Array.from({ length: 6 }, (_, i) => 'B' + i)];
    options.afterAdd = room => room.members.push('BEEF');
    assert.throws(() => h.JoinRoom(args), /Room is full/);
    assert.equal(group(ROOM).members.length, 8);
    assert.equal(group(ROOM).members.some(id => sameId(id, GUEST)), false);
    options.afterAdd = null;

    const legacy = '2'.repeat(32);
    group(REGISTRY).data[legacy] = { Value: JSON.stringify({ roomName: 'Legacy', playerCount: 1 }) };
    groups.set(legacy, { members: [HOST], data: { PlayerCount: { Value: '1' } } });
    group(REGISTRY).data['old-empty-room'] = { Value: 'Old room' };
    before = state();
    assert.equal(h.GetActiveRoomInfos({}).roomInfos[legacy], undefined);
    assert.equal(h.GetActiveRooms({}).rooms[legacy], undefined);
    assert.equal(state(), before, 'Listing must not delete legacy/stale records');

    for (const name of ['AdminValidateRoomKey', 'AdminDeleteRoom', 'AdminClearRoomRegistry'])
        assert.throws(() => h[name]({ ...args, adminKey: 'public-test-key' }), /Title Internal Data/);
    options.readFailure = true;
    assert.throws(() => h.AdminValidateRoomKey({ adminKey: 'public-test-key' }), /Title Internal Data/);
    options.readFailure = false;
    options.internalKey = 'internal-test-key';
    assert.throws(() => h.AdminValidateRoomKey({ adminKey: 'public-test-key' }), /Invalid adminKey/);
    assert.equal(h.AdminValidateRoomKey({ adminKey: 'internal-test-key' }).ok, true);
    assert.equal(calls.publicRead, 0);

    let disabled = 0;
    for (const name of Object.keys(h).filter(name => !ALLOWED.includes(name))) {
        assert.throws(() => h[name]({ monstersKilled: 999, Data: { eventType: 'playerMove' } },
            { playStreamEvent: { StatisticValue: 999 }, playerProfile: { DisplayName: 'Test' } }), /handler is disabled/);
        disabled++;
    }
    assert.equal(disabled > 0, file === 'combinedCloudScript.js');
    assert.equal(calls.sampleWrite, 0);

    for (const failure of [new Error('Timeout'), { error: 'NotAuthorized', errorCode: 1089 },
        { apiErrorInfo: { apiError: 'ProductDisabledForTitle', apiErrorCode: 1609 } }]) {
        group(ROOM).members = [HOST, GUEST];
        c.currentPlayerId = GUEST;
        options.readErrorAfterRemove = failure;
        const registryBefore = clone(group(REGISTRY).data);
        const roomDataBefore = clone(group(ROOM).data);
        assert.throws(() => h.LeaveRoom(args), error => error === failure);
        assert.deepEqual(group(REGISTRY).data, registryBefore, 'Other lookup errors must not delete registry entries');
        assert.deepEqual(group(ROOM).data, roomDataBefore, 'Other lookup errors must not publish a zero count');
    }
    options.readErrorAfterRemove = null;

    group(ROOM).members = [HOST];
    c.currentPlayerId = HOST;
    assert.equal(h.LeaveRoom(args).playerCount, 0);
    assert.equal(groups.has(ROOM), false, 'Last-member removal deletes the actual group and its data');
    assert.equal(group(REGISTRY).data[ROOM], undefined);
    assert.equal(group(REGISTRY).data['ROOMCLOSED_' + ROOM].Value, 'true');
    before = state();
    assert.equal(h.LeaveRoom(args).playerCount, 0);
    assert.equal(state(), before, 'Last-member leave retries must not change other registry entries');
    assert.equal(h.AdminDeleteRoom({ roomId: legacy, adminKey: 'internal-test-key' }).removedCount, 1);
    assert.equal(group(REGISTRY).data[legacy], undefined, 'Explicit authenticated administrator cleanup remains available');
    console.log(file + ': owner namespace, retry, membership/capacity, hidden legacy rooms, internal admin key, connection approval/verification and ' + disabled + ' disabled sample handlers PASS');
}

module.exports = { load, HOST, GUEST, REGISTRY, ROOM, clone, sameId };
