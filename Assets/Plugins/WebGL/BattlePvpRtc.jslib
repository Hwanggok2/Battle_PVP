mergeInto(LibraryManager.library, {
  BattlePvpRtc_Create: function (offer, stunPtr) {
    if (typeof RTCPeerConnection === 'undefined') return 0;
    var registry = Module['battlePvpRtc'] || (Module['battlePvpRtc'] = { next: 1, peers: {} });
    if (Object.keys(registry.peers).length >= 8) return 0;
    var id = registry.next++;
    var s = { closed: false, failed: false, ready: false, checking: false, nextStats: 0, disconnectedAt: 0,
      queue: [], queuedBytes: 0, channels: [], pc: null };
    try {
      s.pc = new RTCPeerConnection({ iceServers: [{ urls: UTF8ToString(stunPtr) }] });
      registry.peers[id] = s;
      s.push = function (kind, data) {
        if (s.closed || s.failed) return;
        data = data || new Uint8Array(0);
        if (data.byteLength > 60000 || s.queue.length >= 1024 || s.queuedBytes + data.byteLength > 1048576) { s.failed = true; return; }
        s.queue.push({ kind: kind, data: data }); s.queuedBytes += data.byteLength;
      };
      s.checkReady = async function () {
        if (s.closed || s.failed || s.ready || s.checking || performance.now() < s.nextStats ||
            s.channels.length !== 2 || s.channels.some(function (c) { return c.readyState !== 'open'; })) return;
        s.checking = true; s.nextStats = performance.now() + 200;
        try {
          var stats = await s.pc.getStats();
          if (s.closed) return;
          stats.forEach(function (entry) {
            if (entry.type !== 'transport' || !entry.selectedCandidatePairId) return;
            var pair = stats.get(entry.selectedCandidatePairId);
            var candidate = pair && stats.get(pair.localCandidateId);
            if (!candidate) return;
            if (candidate.protocol !== 'udp') { s.failed = true; return; }
            s.ready = true; s.push(2);
          });
        } catch (_) { if (!s.closed) s.failed = true; }
        finally { s.checking = false; }
      };
      for (var channel = 0; channel < 2; channel++) {
        (function (index) {
          var options = { negotiated: true, id: index, ordered: index === 0 };
          if (index === 1) options.maxRetransmits = 0;
          var dc = s.pc.createDataChannel(index === 0 ? 'reliable' : 'unreliable', options);
          dc.binaryType = 'arraybuffer';
          dc.onmessage = function (event) { if (!s.closed) s.push(4 + index, new Uint8Array(event.data)); };
          dc.onclose = dc.onerror = function () { if (!s.closed) s.failed = true; };
          dc.onopen = function () { s.checkReady(); };
          s.channels.push(dc);
        })(channel);
      }
      s.pc.onconnectionstatechange = function () {
        if (s.closed) return;
        if (s.pc.connectionState === 'failed' || s.pc.connectionState === 'closed') s.failed = true;
        if (s.pc.connectionState === 'disconnected') s.disconnectedAt = performance.now();
        else s.disconnectedAt = 0;
        s.checkReady();
      };
      s.describe = async function (isOffer, remote) {
        try {
          if (remote !== null) {
            await s.pc.setRemoteDescription({ type: isOffer ? 'answer' : 'offer', sdp: remote });
            if (s.closed || isOffer) return;
          }
          var description = isOffer ? await s.pc.createOffer() : await s.pc.createAnswer();
          if (s.closed) return;
          await s.pc.setLocalDescription(description);
          var deadline = performance.now() + 3000;
          while (!s.closed && s.pc.iceGatheringState !== 'complete' && performance.now() < deadline)
            await new Promise(function (resolve) { setTimeout(resolve, 50); });
          if (!s.closed) s.push(1, new TextEncoder().encode(s.pc.localDescription.sdp));
        } catch (_) { if (!s.closed) s.failed = true; }
      };
      if (offer) s.describe(true, null);
      return id;
    } catch (_) {
      s.closed = true;
      if (s.pc) s.pc.close();
      delete registry.peers[id];
      return 0;
    }
  },
  BattlePvpRtc_Remote: function (id, sdpPtr, offer) {
    var s = Module['battlePvpRtc'] && Module['battlePvpRtc'].peers[id];
    if (s && !s.closed) s.describe(!offer, UTF8ToString(sdpPtr));
  },
  BattlePvpRtc_Poll: function (id, output, capacity) {
    var s = Module['battlePvpRtc'] && Module['battlePvpRtc'].peers[id];
    if (!s || s.closed || s.failed) return -1;
    if (s.disconnectedAt && performance.now() - s.disconnectedAt > 3000) { s.failed = true; return -1; }
    s.checkReady();
    if (!s.queue.length) return 0;
    var entry = s.queue.shift(); s.queuedBytes -= entry.data.byteLength;
    if (entry.data.byteLength + 1 > capacity) { s.failed = true; return -1; }
    HEAPU8[output] = entry.kind;
    HEAPU8.set(entry.data, output + 1);
    return entry.data.byteLength + 1;
  },
  BattlePvpRtc_Send: function (id, data, offset, count, channel) {
    var s = Module['battlePvpRtc'] && Module['battlePvpRtc'].peers[id];
    if (!s || s.closed || s.failed || !s.ready || channel < 0 || channel > 1 || count < 0 || count > 60000) return 2;
    var dc = s.channels[channel];
    if (dc.readyState !== 'open') return 2;
    if (dc.bufferedAmount + count > 262144) return 1;
    try { dc.send(HEAPU8.slice(data + offset, data + offset + count)); return 0; }
    catch (_) { s.failed = true; return 2; }
  },
  BattlePvpRtc_Close: function (id) {
    var registry = Module['battlePvpRtc'], s = registry && registry.peers[id];
    if (!s) return;
    s.closed = true; s.queue.length = 0; s.queuedBytes = 0;
    s.channels.forEach(function (dc) { dc.onmessage = dc.onopen = dc.onclose = dc.onerror = null; dc.close(); });
    s.pc.onconnectionstatechange = null; s.pc.close();
    delete registry.peers[id];
  }
});
