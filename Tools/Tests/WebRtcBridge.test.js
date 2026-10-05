const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('Assets/Plugins/WebGL/BattlePvpRtc.jslib', 'utf8');
function fixture(protocol = 'udp') {
  const peers = [], lib = {};
  class Connection {
    constructor() { this.channels = []; this.connectionState = 'connected'; peers.push(this); }
    createDataChannel(label, options) {
      const dc = { label, options, readyState: 'open', bufferedAmount: 0, sent: [],
        send(bytes) { this.sent.push([...bytes]); }, close() { this.readyState = 'closed'; } };
      this.channels.push(dc); return dc;
    }
    getStats() { return Promise.resolve(new Map([
      ['transport', { type: 'transport', selectedCandidatePairId: 'pair' }],
      ['pair', { localCandidateId: 'local' }], ['local', { protocol }]
    ])); }
    close() { this.connectionState = 'closed'; }
  }
  const context = { Module: {}, LibraryManager: { library: lib }, mergeInto: Object.assign,
    RTCPeerConnection: Connection, UTF8ToString: () => 'stun:test', HEAPU8: new Uint8Array(70000),
    TextEncoder, Uint8Array, performance: { now: () => 1000 }, setTimeout };
  vm.runInNewContext(source, context);
  const id = lib.BattlePvpRtc_Create(0, 1);
  return { lib, context, id, peer: peers[0] };
}
const flush = () => new Promise(resolve => setImmediate(resolve));
test('uses separate ordered reliable and non-retransmitted movement channels', () => {
  const f = fixture();
  assert.equal(f.peer.channels[0].options.ordered, true);
  assert.equal(f.peer.channels[0].options.maxRetransmits, undefined);
  assert.equal(f.peer.channels[1].options.ordered, false);
  assert.equal(f.peer.channels[1].options.maxRetransmits, 0);
  f.lib.BattlePvpRtc_Close(f.id);
  assert.equal(Object.keys(f.context.Module.battlePvpRtc.peers).length, 0);
  assert.equal(f.peer.connectionState, 'closed');
});
test('only marks UDP ready and copies send subranges', async () => {
  const f = fixture(); f.lib.BattlePvpRtc_Poll(f.id, 100, 60001); await flush();
  assert.equal(f.lib.BattlePvpRtc_Poll(f.id, 100, 60001), 1);
  assert.equal(f.context.HEAPU8[100], 2);
  f.context.HEAPU8.set([1, 2, 3, 4], 500);
  assert.equal(f.lib.BattlePvpRtc_Send(f.id, 500, 1, 2, 0), 0);
  assert.deepEqual(f.peer.channels[0].sent[0], [2, 3]);
  f.context.HEAPU8[501] = 9;
  assert.deepEqual(f.peer.channels[0].sent[0], [2, 3]);
  f.peer.channels[0].bufferedAmount = 262144;
  assert.equal(f.lib.BattlePvpRtc_Send(f.id, 500, 0, 1, 0), 1);
});
test('refuses TCP candidate so caller retains Relay', async () => {
  const f = fixture('tcp'); f.lib.BattlePvpRtc_Poll(f.id, 0, 60001); await flush();
  assert.equal(f.lib.BattlePvpRtc_Poll(f.id, 0, 60001), -1);
});
test('receive queue is bounded and late events are ignored after close', () => {
  const f = fixture(); const receive = f.peer.channels[0].onmessage;
  for (let i = 0; i < 1025; i++) receive({ data: new Uint8Array([1]).buffer });
  assert.equal(f.lib.BattlePvpRtc_Poll(f.id, 0, 60001), -1);
  f.lib.BattlePvpRtc_Close(f.id); receive({ data: new Uint8Array([1]).buffer });
  assert.equal(Object.keys(f.context.Module.battlePvpRtc.peers).length, 0);
});
