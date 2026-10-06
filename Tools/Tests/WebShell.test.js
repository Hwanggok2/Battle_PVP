const assert = require('node:assert/strict');
const { test } = require('node:test');
const { create } = require('../../Assets/WebGLTemplates/BattlePvp/shell.js');

function fixture({ keyboard = true, denyLock = false, denyFullscreen = false, renderer = 'ANGLE NVIDIA GPU' } = {}) {
    function element() {
        const handlers = {}, classes = new Set();
        return { hidden: true, width: 960, height: 600, textContent: '', attributes: {},
            classList: { toggle(name, value) { value ? classes.add(name) : classes.delete(name); }, contains: name => classes.has(name) },
            addEventListener(name, fn) { (handlers[name] ||= []).push(fn); },
            emit(name, event = {}) { for (const fn of handlers[name] || []) fn(event); },
            setAttribute(k, v) { this.attributes[k] = v; }, focus() {} };
    }
    const elements = new Map(), doc = element(), win = element(); let locks = [], unlocks = 0, resize = 0, clears = 0;
    doc.getElementById = id => { if (!elements.has(id)) elements.set(id, element()); return elements.get(id); };
    doc.fullscreenElement = null;
    const container = doc.getElementById('unity-container'), canvas = doc.getElementById('unity-canvas');
    container.requestFullscreen = async () => { if (denyFullscreen) throw Error('denied'); doc.fullscreenElement = container; doc.emit('fullscreenchange'); };
    doc.exitFullscreen = async () => { doc.fullscreenElement = null; doc.emit('fullscreenchange'); };
    const nav = keyboard ? { keyboard: { async lock(keys) { locks.push(keys); if (denyLock) throw Error('denied'); }, unlock() { unlocks++; } } } : {};
    win.requestAnimationFrame = fn => fn(); win.Event = class { constructor(type) { this.type = type; } };
    win.dispatchEvent = () => { resize++; }; win.setInterval = () => 1; win.clearInterval = () => { clears++; };
    canvas.getContext = () => ({ getExtension: () => ({ UNMASKED_RENDERER_WEBGL: 1 }), getParameter: () => renderer });
    const shell = create(doc, win, nav);
    return { shell, doc, win, container, canvas, el: doc.getElementById, locks, get unlocks() { return unlocks; }, get resizes() { return resize; }, get clears() { return clears; } };
}

test('short Escape stays fullscreen and propagates to the game cursor handler', async () => {
    const f = fixture(); await f.shell.toggleFullscreen();
    assert.deepEqual(f.locks, [['Escape']]); assert.equal(f.doc.fullscreenElement, f.container);
    let prevented = 0;
    f.doc.emit('keydown', { code: 'Escape', preventDefault() { prevented++; }, stopPropagation() { throw Error('Must reach Unity'); } });
    assert.equal(prevented, 1); assert.equal(f.doc.fullscreenElement, f.container);
    assert.equal(f.container.classList.contains('expanded'), true);
});

for (const options of [{ keyboard: false }, { denyLock: true }, { denyFullscreen: true }]) {
    test('unsupported or rejected native fullscreen falls back to page expansion ' + JSON.stringify(options), async () => {
        const f = fixture(options); await f.shell.toggleFullscreen();
        assert.equal(f.doc.fullscreenElement, null); assert.equal(f.container.classList.contains('expanded'), true);
        assert.match(f.el('display-hint').textContent, /페이지 확대/);
        await f.shell.toggleFullscreen(); assert.equal(f.container.classList.contains('expanded'), false);
    });
}

test('browser exit restores layout without resetting or replacing the WebGL canvas', async () => {
    const f = fixture(); await f.shell.toggleFullscreen(); await f.doc.exitFullscreen();
    assert.equal(f.container.classList.contains('expanded'), false);
    assert.equal(f.el('fullscreen-button').textContent, '전체화면');
    assert.equal(f.canvas.width, 960); assert.equal(f.canvas.height, 600);
    assert.ok(f.resizes > 0); assert.ok(f.unlocks > 0);
});

test('renderer diagnostic distinguishes software fallback and reads real Unity frame metrics', () => {
    const f = fixture({ renderer: 'ANGLE SwiftShader Device' });
    f.shell.attach({ GetMetricsInfo: () => ({ movingAverageFps: 42.25, usedWASMHeapSize: 104857600 }) });
    assert.match(f.el('renderer-info').textContent, /소프트웨어/);
    assert.match(f.el('game-message').textContent, /그래픽 가속/);
    f.el('performance-button').emit('click');
    assert.match(f.el('frame-info').textContent, /42.3 FPS/); assert.match(f.el('frame-info').textContent, /100 MB/);
    f.win.emit('pagehide'); assert.equal(f.clears, 1);
});

test('GPU rendering does not display a software warning and context recovery clears its message', () => {
    const f = fixture(); f.shell.attach({});
    assert.match(f.el('renderer-info').textContent, /GPU 렌더링/); assert.equal(f.el('game-message').hidden, true);
    f.canvas.emit('webglcontextlost', { preventDefault() {} }); assert.equal(f.el('game-message').hidden, false);
    f.canvas.emit('webglcontextrestored'); assert.equal(f.el('game-message').hidden, true);
});
