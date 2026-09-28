const assert = require('node:assert/strict');
const { load, REGISTRY, ROOM } = require('./NetworkProfileRoomCapacity.test.js');

const collision = () => ({ message: 'PlayFab API request error',
    apiErrorInfo: { api: '/Server/CreateSharedGroup',
        apiError: { error: 'InvalidSharedGroupId', errorCode: 1088 } } });
const unavailable = { apiErrorInfo: { apiError: { error: 'ServiceUnavailable', errorCode: 1123 } } };
let scenarios = 0;

for (const file of ['roomRegistry.js', 'combinedCloudScript.js']) {
    const run = (name, test) => {
        try { test(load(file)); scenarios++; }
        catch (error) { throw new Error(file + ' / ' + name + ': ' + (error.stack || JSON.stringify(error))); }
    };
    run('live nested error payload is recognized without matching generic wrapper messages', f => {
        assert.equal(f.context.isMissingSharedGroup(collision()), true);
        assert.equal(f.context.isMissingSharedGroup(unavailable), false);
        assert.equal(f.context.isMissingSharedGroup(new Error('PlayFab API request error')), false);
        assert.equal(f.context.isMissingSharedGroup(null), false);
    });
    run('legacy flat errors and direct API errors still work', f => {
        for (const error of [
            { apiErrorInfo: { apiError: 'InvalidSharedGroupId', apiErrorCode: 1088 } },
            { error: 'InvalidSharedGroupId', errorCode: 1088 },
            'SharedGroupNotFound'
        ]) assert.equal(f.context.isMissingSharedGroup(error), true);
    });
    run('first creation and repeated registration preserve existing members', f => {
        const args = { roomId: ROOM, roomName: 'Regression room', relayJoinCode: 'RELAY1' };
        assert.equal(f.context.handlers.RegisterRoomToRegistry(args).ok, true);
        f.group(ROOM).members.push('DEF456');
        f.context.server.CreateSharedGroup = () => { throw new Error('Existing groups must not be recreated'); };
        assert.equal(f.context.handlers.RegisterRoomToRegistry(args).roomInfo.playerCount, 2);
        assert.deepEqual(f.group(ROOM).members, ['ABC123', 'DEF456']);
    });
    run('existing registry data is preserved without any creation attempt', f => {
        f.groups.set(REGISTRY, { members: [], data: { existing: { Value: 'preserve' } } });
        f.context.server.CreateSharedGroup = () => { throw new Error('Unexpected create'); };
        f.context.ensureSharedGroup(REGISTRY);
        assert.equal(f.group(REGISTRY).data.existing.Value, 'preserve');
    });
    run('concurrent creation with actual PlayFab 1088 is confirmed by a read', f => {
        let reads = 0;
        const read = f.context.server.GetSharedGroupData;
        f.context.server.GetSharedGroupData = args => { reads++; return read(args); };
        f.context.server.CreateSharedGroup = ({ SharedGroupId }) => {
            f.groups.set(SharedGroupId, { members: [], data: {} });
            throw collision();
        };
        f.context.ensureSharedGroup(REGISTRY);
        assert.equal(reads, 2);
        assert.equal(f.groups.has(REGISTRY), true);
    });
    run('1088 without an existing group is not accepted', f => {
        f.context.server.CreateSharedGroup = () => { throw collision(); };
        assert.throws(() => f.context.ensureSharedGroup(REGISTRY));
        assert.equal(f.groups.size, 0);
    });
    run('lookup outage does not attempt creation', f => {
        f.context.server.GetSharedGroupData = () => { throw unavailable; };
        let created = false;
        f.context.server.CreateSharedGroup = () => { created = true; };
        assert.throws(() => f.context.ensureSharedGroup(REGISTRY), error => error === unavailable);
        assert.equal(created, false);
    });
    run('creation outage propagates without treating it as an ID collision', f => {
        f.context.server.CreateSharedGroup = () => { throw unavailable; };
        assert.throws(() => f.context.ensureSharedGroup(REGISTRY), error => error === unavailable);
        assert.equal(f.groups.size, 0);
    });
    run('failed confirmation read is not accepted after a creation race', f => {
        f.context.server.CreateSharedGroup = () => {
            f.context.server.GetSharedGroupData = () => { throw unavailable; };
            throw collision();
        };
        assert.throws(() => f.context.ensureSharedGroup(REGISTRY), error => error === unavailable);
    });
}
console.log(`PASS: ${scenarios} shared-group creation scenarios against both CloudScript files.`);
