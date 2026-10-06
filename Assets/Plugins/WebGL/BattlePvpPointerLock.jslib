mergeInto(LibraryManager.library, {
  BattlePvpWeb_ToggleFullscreen: function () {
    // Use the same Escape-preserving path for both the HTML and in-game buttons.
    if (window.__battlePvpWebShell) window.__battlePvpWebShell.toggleFullscreen();
  },
  BattlePvpPointerLock_SetEnabled: function (enabled) {
    var state = Module['battlePvpPointerLock'];
    if (!state) {
      var canvas = Module['canvas'];
      state = { enabled: false, pending: false, disposed: false, canvas: canvas,
        wasLocked: false, unlockEvent: false, awaitingUnlockAck: false };
      Module['battlePvpPointerLock'] = state;

      var finish = function () {
        state.pending = false;
        // A menu or scene may have opened while the browser request was pending.
        if ((!state.enabled || state.disposed) && document.pointerLockElement === canvas)
          document.exitPointerLock();
      };
      state.onChange = function () {
        var locked = document.pointerLockElement === canvas;
        // Browsers can consume Escape without delivering a key event to Unity.
        // Keep the next UI click free until Unity has entered cursor mode.
        if (state.wasLocked && !locked && state.enabled && !state.disposed) {
          state.unlockEvent = true;
          state.awaitingUnlockAck = true;
          state.enabled = false;
        }
        state.wasLocked = locked;
        finish();
      };
      state.onError = function () { state.pending = false; };
      state.onPointerDown = function (event) {
        if (!state.enabled || state.disposed || state.pending || !event.isTrusted ||
            event.pointerType !== 'mouse' || (event.button !== 0 && event.button !== 2) ||
            document.visibilityState !== 'visible' || document.pointerLockElement === canvas ||
            (navigator.userActivation && !navigator.userActivation.isActive) ||
            !canvas.requestPointerLock) return;
        state.pending = true;
        try {
          // Do not defer: the browser must still be handling the actual player input.
          var request = canvas.requestPointerLock();
          if (request && typeof request.then === 'function')
            request.then(finish, state.onError);
        } catch (_) {
          // A denied lock is recoverable. Retry only on the player's next click.
          state.onError();
        }
      };
      canvas.addEventListener('pointerdown', state.onPointerDown);
      document.addEventListener('pointerlockchange', state.onChange);
      document.addEventListener('pointerlockerror', state.onError);
    }
    if (!enabled) state.awaitingUnlockAck = false;
    state.enabled = !!enabled && !state.awaitingUnlockAck;
    if (!state.enabled && document.pointerLockElement === state.canvas)
      document.exitPointerLock();
  },

  BattlePvpPointerLock_IsLocked: function () {
    return document.pointerLockElement === Module['canvas'] ? 1 : 0;
  },

  BattlePvpPointerLock_ConsumeUnlock: function () {
    var state = Module['battlePvpPointerLock'];
    if (!state || !state.unlockEvent) return 0;
    state.unlockEvent = false;
    return 1;
  },

  BattlePvpPointerLock_Dispose: function () {
    var state = Module['battlePvpPointerLock'];
    if (!state) return;
    state.enabled = false;
    state.disposed = true;
    state.canvas.removeEventListener('pointerdown', state.onPointerDown);
    document.removeEventListener('pointerlockchange', state.onChange);
    document.removeEventListener('pointerlockerror', state.onError);
    if (document.pointerLockElement === state.canvas) document.exitPointerLock();
    delete Module['battlePvpPointerLock'];
  }
});
