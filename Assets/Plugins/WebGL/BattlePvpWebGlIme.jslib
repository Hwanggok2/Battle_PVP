mergeInto(LibraryManager.library, {
  BattlePvpWebGlIme_Open: function (receiverNamePtr, initialTextPtr, maxLength) {
    var receiverName = UTF8ToString(receiverNamePtr);
    var initialText = UTF8ToString(initialTextPtr);
    var state = window.__battlePvpWebGlIme || {};
    var input = state.input;

    if (!input) {
      input = document.createElement('input');
      input.type = 'text';
      input.id = 'battle-pvp-webgl-ime';
      input.autocomplete = 'off';
      input.autocapitalize = 'off';
      input.spellcheck = false;
      input.inputMode = 'text';
      input.setAttribute('lang', 'ko');
      input.setAttribute('aria-label', 'Game chat input');
      input.style.position = 'fixed';
      input.style.left = '24px';
      input.style.bottom = '42px';
      input.style.width = 'min(420px, calc(100vw - 48px))';
      input.style.height = '34px';
      input.style.boxSizing = 'border-box';
      input.style.padding = '4px 10px';
      input.style.opacity = '1';
      input.style.zIndex = '2147483647';
      input.style.color = '#ffffff';
      input.style.background = 'rgba(15, 15, 15, 0.92)';
      input.style.border = '1px solid rgba(255, 255, 255, 0.75)';
      input.style.borderRadius = '2px';
      input.style.outline = 'none';
      input.style.caretColor = '#ffffff';
      input.style.font = '16px sans-serif';
      document.body.appendChild(input);
      state.input = input;
    }

    state.receiverName = receiverName;
    state.isComposing = false;
    state.fieldMode = false;
    var generation = state.generation = (state.generation || 0) + 1;
    window.__battlePvpWebGlIme = state;
    input.style.left = '24px';
    input.style.top = 'auto';
    input.style.bottom = '42px';
    input.style.width = 'min(420px, calc(100vw - 48px))';
    input.style.height = '34px';
    input.setAttribute('aria-label', 'Game chat input');
    input.value = initialText || '';
    input.maxLength = maxLength > 0 ? maxLength : 524288;
    input.style.display = 'block';

    input.oninput = function () {
      SendMessage(state.receiverName, 'OnWebGlInputChanged', input.value);
    };

    input.oncompositionstart = function () {
      state.isComposing = true;
    };

    input.oncompositionend = function () {
      state.isComposing = false;
      SendMessage(state.receiverName, 'OnWebGlInputChanged', input.value);
    };

    input.onkeydown = function (event) {
      event.stopPropagation();
      if (event.key === 'Enter' && !event.isComposing && !state.isComposing) {
        event.preventDefault();
        SendMessage(state.receiverName, 'OnWebGlInputSubmitted', input.value);
      } else if (event.key === 'Escape') {
        event.preventDefault();
        SendMessage(state.receiverName, 'OnWebGlInputCancelled', '');
      }
    };

    input.onblur = function () {
      if (state.fieldMode && input.style.display !== 'none')
        SendMessage(state.receiverName, 'OnWebGlInputBlurred', input.value);
    };

    var focusInput = function () {
      if (input.style.display === 'none' || state.generation !== generation) return;
      try {
        input.focus({ preventScroll: true });
      } catch (_) {
        input.focus();
      }

      if (document.activeElement === input)
        input.setSelectionRange(input.value.length, input.value.length);
    };

    window.setTimeout(focusInput, 0);
    window.setTimeout(focusInput, 50);
    window.setTimeout(focusInput, 150);
  },

  BattlePvpWebGlIme_SetRect: function (x, y, width, height) {
    var state = window.__battlePvpWebGlIme;
    if (!state || !state.input) return;
    var rect = Module['canvas'].getBoundingClientRect();
    state.fieldMode = true;
    state.input.setAttribute('aria-label', 'Room name');
    state.input.style.left = (rect.left + x * rect.width) + 'px';
    state.input.style.top = (rect.top + y * rect.height) + 'px';
    state.input.style.bottom = 'auto';
    state.input.style.width = Math.max(1, width * rect.width) + 'px';
    state.input.style.height = Math.max(1, height * rect.height) + 'px';
  },

  BattlePvpWebGlIme_Close: function () {
    var state = window.__battlePvpWebGlIme;
    if (!state || !state.input)
      return;

    state.input.oninput = null;
    state.input.onkeydown = null;
    state.input.oncompositionstart = null;
    state.input.oncompositionend = null;
    state.input.onblur = null;
    state.isComposing = false;
    state.generation++;
    state.input.blur();
    state.input.style.display = 'none';
  }
});
