(function (root) {
  'use strict';
  function create(doc, win, nav) {
    const container = doc.getElementById('unity-container');
    const canvas = doc.getElementById('unity-canvas');
    const fullscreen = doc.getElementById('fullscreen-button');
    const hint = doc.getElementById('display-hint');
    const message = doc.getElementById('game-message');
    const panel = doc.getElementById('performance-info');
    let mode = 'window', busy = false, instance = null, timer = null;
    const supportsLock = !!(nav.keyboard && nav.keyboard.lock && nav.keyboard.unlock);
    function showMessage(text) { message.textContent = text; message.hidden = false; }
    function resize() { win.requestAnimationFrame(() => win.dispatchEvent(new win.Event('resize'))); }
    function updateLayout() {
      container.classList.toggle('expanded', mode !== 'window');
      fullscreen.textContent = mode === 'window' ? '전체화면' : '화면 축소';
      hint.textContent = mode === 'native' ? 'Esc 커서 전환 · 전체화면 종료는 Esc 길게' :
        mode === 'expanded' ? '페이지 확대 · Esc 커서 전환' : 'Esc 커서 전환';
      resize();
    }
    function unlockKeyboard() { if (supportsLock) nav.keyboard.unlock(); }
    async function toggleFullscreen() {
      if (busy) return;
      busy = true;
      try {
        if (mode !== 'window') {
          mode = 'window'; unlockKeyboard();
          if (doc.fullscreenElement === container) await doc.exitFullscreen();
        } else {
          mode = 'expanded'; updateLayout();
          if (supportsLock && container.requestFullscreen) {
            try {
              mode = 'native';
              await container.requestFullscreen({ navigationUI: 'hide' });
              await nav.keyboard.lock(['Escape']);
              // The player may have exited while the permission request was pending.
              if (doc.fullscreenElement !== container) { unlockKeyboard(); mode = 'window'; }
            } catch (_) {
              mode = 'expanded'; unlockKeyboard();
              if (doc.fullscreenElement === container) await doc.exitFullscreen();
            }
          }
        }
      } catch (_) { mode = 'window'; }
      finally { busy = false; updateLayout(); canvas.focus({ preventScroll: true }); }
    }
    fullscreen.addEventListener('click', toggleFullscreen);
    doc.addEventListener('fullscreenchange', () => {
      if (doc.fullscreenElement !== container && mode === 'native') { mode = 'window'; unlockKeyboard(); }
      updateLayout();
    });
    doc.addEventListener('keydown', event => {
      // Preserve propagation to Unity so its existing Escape handler releases the cursor.
      if (event.code === 'Escape' && mode === 'native') event.preventDefault();
    });
    canvas.addEventListener('webglcontextlost', event => {
      event.preventDefault();
      showMessage('그래픽 연결이 끊어졌습니다. 복구되지 않으면 페이지를 새로고침해 주세요.');
    });
    canvas.addEventListener('webglcontextrestored', () => { message.hidden = true; resize(); });
    const performanceButton = doc.getElementById('performance-button');
    performanceButton.addEventListener('click', () => {
      panel.hidden = !panel.hidden;
      performanceButton.setAttribute('aria-expanded', String(!panel.hidden));
      updateMetrics();
    });
    function inspectRenderer() {
      const gl = canvas.getContext('webgl2');
      if (!gl) return;
      const extension = gl.getExtension('WEBGL_debug_renderer_info');
      const renderer = String(gl.getParameter(extension ? extension.UNMASKED_RENDERER_WEBGL : gl.RENDERER));
      const software = /swiftshader|llvmpipe|softpipe|software|microsoft basic render/i.test(renderer);
      const label = extension ? (software ? '소프트웨어 렌더링' : 'GPU 렌더링') : '렌더러 정보 제한됨';
      doc.getElementById('renderer-info').textContent = label + ' · ' + renderer;
      if (software) showMessage('소프트웨어 렌더링이 감지됐습니다. 브라우저 설정의 그래픽 가속을 켠 뒤 브라우저를 다시 시작해 주세요.');
    }
    function updateMetrics() {
      if (panel.hidden || !instance || !instance.GetMetricsInfo) return;
      const metrics = instance.GetMetricsInfo();
      const fps = Number(metrics.movingAverageFps);
      doc.getElementById('frame-info').textContent = (Number.isFinite(fps) ? fps.toFixed(1) : '—') +
        ' FPS · ' + canvas.width + ' × ' + canvas.height + ' · 메모리 ' + Math.round(metrics.usedWASMHeapSize / 1048576) + ' MB';
    }
    win.addEventListener('pagehide', () => { unlockKeyboard(); if (timer !== null) win.clearInterval(timer); });
    return {
      showMessage, toggleFullscreen,
      attach(player) {
        instance = player;
        doc.getElementById('loading').hidden = true;
        inspectRenderer(); updateMetrics();
        timer = win.setInterval(updateMetrics, 1000);
      }
    };
  }
  root.BattlePvpWebShell = { create };
  if (typeof module !== 'undefined' && module.exports) module.exports = root.BattlePvpWebShell;
})(typeof window !== 'undefined' ? window : globalThis);
