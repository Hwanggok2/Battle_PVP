const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');
const source = fs.readFileSync(path.join(__dirname, '../../Assets/Plugins/WebGL/BattlePvpWebGlIme.jslib'), 'utf8');

function fixture() {
    const library = {}, messages = [], timers = [], document = { activeElement: null };
    const input = { style: {}, attributes: {}, value: '', focuses: 0,
        setAttribute(key, value) { this.attributes[key] = value; },
        focus() { document.activeElement = input; this.focuses++; },
        blur() { document.activeElement = null; this.onblur?.(); }, setSelectionRange() {} };
    document.createElement = () => input;
    document.body = { appendChild() {} };
    const window = { setTimeout(fn) { timers.push(fn); } };
    vm.runInNewContext(source, { LibraryManager: { library }, mergeInto: Object.assign, document, window,
        Module: { canvas: { getBoundingClientRect: () => ({ left: 100, top: 50, width: 960, height: 600 }) } },
        UTF8ToString: value => value, SendMessage: (...args) => messages.push(args) });
    return { library, input, messages, timers, window };
}

test('room title supports Hangul composition without submitting the composing Enter', () => {
    const f = fixture(); f.library.BattlePvpWebGlIme_Open('Room', '', 30);
    f.library.BattlePvpWebGlIme_SetRect(.25, .4, .5, .08);
    assert.equal(f.input.style.left, '340px'); assert.equal(f.input.style.top, '290px');
    assert.equal(f.input.attributes['aria-label'], 'Room name');
    f.input.oncompositionstart(); f.input.value = '한글 대기실'; f.input.oninput();
    const enter = { key: 'Enter', isComposing: true, stopPropagation() {}, preventDefault() {} };
    f.input.onkeydown(enter);
    assert.equal(f.messages.filter(x => x[1] === 'OnWebGlInputSubmitted').length, 0);
    f.input.oncompositionend(); f.input.onkeydown({ ...enter, isComposing: false });
    assert.deepEqual(f.messages.at(-1), ['Room', 'OnWebGlInputSubmitted', '한글 대기실']);
});

test('clicking save commits the room name on blur, while chat keeps its existing behavior', () => {
    const f = fixture(); f.library.BattlePvpWebGlIme_Open('Room', '', 30);
    f.library.BattlePvpWebGlIme_SetRect(0, 0, 1, .1);
    f.input.value = '웹 검증'; f.input.blur();
    assert.deepEqual(f.messages.at(-1), ['Room', 'OnWebGlInputBlurred', '웹 검증']);
    f.library.BattlePvpWebGlIme_Close();
    f.library.BattlePvpWebGlIme_Open('Chat', '', 200); f.messages.length = 0;
    f.input.blur(); assert.equal(f.messages.length, 0);
    assert.equal(f.input.style.top, 'auto'); assert.equal(f.input.style.bottom, '42px');
});

test('closing a field cancels delayed refocusing and removes all input callbacks', () => {
    const f = fixture(); f.library.BattlePvpWebGlIme_Open('Room', '', 30);
    f.library.BattlePvpWebGlIme_Close();
    f.timers.forEach(fn => fn());
    assert.equal(f.input.focuses, 0); assert.equal(f.input.onblur, null);
    assert.equal(f.input.oninput, null); assert.equal(f.input.style.display, 'none');
});
