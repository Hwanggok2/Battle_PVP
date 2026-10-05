const assert = require('node:assert/strict');
const { load } = require('./NetworkProfileRoomCapacity.test.js');
for (const file of ['roomRegistry.js', 'combinedCloudScript.js']) {
    const { context: c } = load(file), h = c.handlers;
    const roomId = 'battle_abc123_' + '1'.repeat(32);
    const args = { roomId, roomName: 'UDP fixture', masterName: 'Host', relayJoinCode: 'relay', directEndpoint: '203.0.113.1:32853' };
    assert.equal(h.RegisterRoomToRegistry(args).roomInfo.directEndpoint, args.directEndpoint);
    c.currentPlayerId = 'DEF456';
    assert.equal(h.JoinRoom({ roomId }).roomInfo.directEndpoint, args.directEndpoint);
    assert.throws(() => h.RegisterRoomToRegistry({ ...args, directEndpoint: '203.0.113.2:4444' }));
    c.currentPlayerId = 'ABC123';
    for (const candidate of ['127.0.0.1:7777', '10.0.0.1:7777', '100.64.0.1:7777', 'example.com:7777', '203.0.113.1:80', '224.0.0.1:7777', '203.0.113.1:65536'])
        assert.throws(() => h.RegisterRoomToRegistry({ ...args, directEndpoint: candidate }), /Invalid direct UDP/);
    assert.equal(h.RegisterRoomToRegistry({ ...args, directEndpoint: '' }).roomInfo.directEndpoint, '');
    assert.equal(h.RegisterRoomToRegistry({ ...args, directEndpoint: undefined }).ok, true);
    console.log(file + ': optional public UDP candidate, owner authorization, validation and old-client compatibility passed');
}
