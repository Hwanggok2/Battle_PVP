const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');
const source = fs.readFileSync(path.join(__dirname, '../../Assets/Plugins/WebGL/BattlePvpPointerLock.jslib'), 'utf8');

function fixture(request) {
    function target() {
        const handlers = new Map();
        return {
            addEventListener(type, fn) { if (!handlers.has(type)) handlers.set(type, new Set()); handlers.get(type).add(fn); },
            removeEventListener(type, fn) { handlers.get(type)?.delete(fn); },
            emit(type, event) { for (const fn of [...(handlers.get(type) || [])]) fn(event); },
            listeners() { return [...handlers.values()].reduce((n, set) => n + set.size, 0); }
        };
    }
    const canvas = target(), document = target(), library = {};
    let requests = 0, exits = 0;
    document.visibilityState = 'visible';
    document.pointerLockElement = null;
    document.exitPointerLock = () => { exits++; document.pointerLockElement = null; document.emit('pointerlockchange'); };
    canvas.requestPointerLock = () => { requests++; return request?.(); };
    const context = { Module: { canvas }, document, navigator: { userActivation: { isActive: true } },
        LibraryManager: { library }, mergeInto: Object.assign };
    vm.runInNewContext(source, context);
    return { ...context, canvas, library, get requests() { return requests; }, get exits() { return exits; },
        click(overrides = {}) { canvas.emit('pointerdown', { isTrusted: true, pointerType: 'mouse', button: 0, ...overrides }); },
        enable(value = true) { library.BattlePvpPointerLock_SetEnabled(+value); },
        lock() { document.pointerLockElement = canvas; document.emit('pointerlockchange'); } };
}

test('frames, menus, synthetic input, touch and inactive tabs never request a lock', () => {
    const f = fixture();
    f.enable(false); f.click();
    for (let i = 0; i < 100; i++) f.enable();
    assert.equal(f.canvas.listeners(), 1);
    assert.equal(f.document.listeners(), 2);
    assert.equal(f.requests, 0);
    f.click({ isTrusted: false }); f.click({ pointerType: 'touch' }); f.click({ button: 1 });
    f.navigator.userActivation.isActive = false; f.click();
    f.navigator.userActivation.isActive = true; f.document.visibilityState = 'hidden'; f.click();
    assert.equal(f.requests, 0);
});

test('a real click requests synchronously and pending requests are not duplicated', () => {
    const f = fixture(); f.enable(); f.click(); f.click();
    assert.equal(f.requests, 1);
    f.lock(); f.click();
    assert.equal(f.requests, 1);
    assert.equal(f.library.BattlePvpPointerLock_IsLocked(), 1);
});

test('a rejected Promise is handled and requires a new click to retry', async () => {
    const f = fixture(() => Promise.reject(new Error('NotAllowedError')));
    f.enable(); f.click(); await new Promise(setImmediate);
    assert.equal(f.requests, 1);
    assert.equal(f.library.BattlePvpPointerLock_IsLocked(), 0);
    f.enable(); assert.equal(f.requests, 1);
    f.click(); await new Promise(setImmediate);
    assert.equal(f.requests, 2);
});

test('synchronous denial and legacy pointerlockerror remain recoverable', () => {
    const denied = fixture(() => { throw new Error('SecurityError'); });
    denied.enable(); assert.doesNotThrow(() => denied.click()); denied.click();
    assert.equal(denied.requests, 2);
    const legacy = fixture(); legacy.enable(); legacy.click();
    legacy.document.emit('pointerlockerror'); legacy.click();
    assert.equal(legacy.requests, 2);
});

test('browser Escape preserves the next UI click until Unity acknowledges cursor mode', () => {
    const f = fixture(); f.enable(); f.click(); f.lock();
    f.document.exitPointerLock();
    for (let i = 0; i < 100; i++) f.enable();
    assert.equal(f.requests, 1);
    f.click(); assert.equal(f.requests, 1);
    assert.equal(f.library.BattlePvpPointerLock_ConsumeUnlock(), 1);
    assert.equal(f.library.BattlePvpPointerLock_ConsumeUnlock(), 0);
    f.enable(); f.click(); assert.equal(f.requests, 1);
    f.enable(false); f.click(); assert.equal(f.requests, 1);
    f.enable(); f.click(); assert.equal(f.requests, 2);
});

test('a deliberate menu release does not become a second Escape event', () => {
    const f = fixture(); f.enable(); f.click(); f.lock(); f.enable(false);
    assert.equal(f.library.BattlePvpPointerLock_ConsumeUnlock(), 0);
    f.enable(); f.click(); assert.equal(f.requests, 2);
});

test('opening a menu releases the pointer; a late successful request is released too', async () => {
    let resolve;
    const f = fixture(() => new Promise(done => { resolve = done; }));
    f.enable(); f.click(); f.enable(false); f.lock(); resolve(); await new Promise(setImmediate);
    assert.equal(f.library.BattlePvpPointerLock_IsLocked(), 0);
    assert.equal(f.exits, 1);
    f.click(); assert.equal(f.requests, 1);
});

test('scene cleanup removes listeners and handles a pending Promise', async () => {
    let resolve;
    const f = fixture(() => new Promise(done => { resolve = done; }));
    f.enable(); f.click(); f.library.BattlePvpPointerLock_Dispose();
    assert.equal(f.canvas.listeners() + f.document.listeners(), 0);
    f.lock(); resolve(); await new Promise(setImmediate);
    assert.equal(f.library.BattlePvpPointerLock_IsLocked(), 0);
    f.enable(); assert.equal(f.canvas.listeners(), 1);
});

test('unsupported browsers leave the cursor available', () => {
    const f = fixture(); delete f.canvas.requestPointerLock;
    f.enable(); assert.doesNotThrow(() => f.click());
    assert.equal(f.library.BattlePvpPointerLock_IsLocked(), 0);
});
