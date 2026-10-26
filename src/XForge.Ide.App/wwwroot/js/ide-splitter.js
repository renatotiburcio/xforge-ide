// Splitter do painel inferior do editor (estilo VS Code).
// Arrasta a borda superior do painel e persiste a altura em localStorage.
window.ideSplitter = (function () {
  var active = null;
  var MIN = 120;
  var MAX_RATIO = 0.75;
  var KEY = 'ide-panel-height';

  function hostOf(handle) {
    return handle.closest('.ide-editor-area') || handle.parentElement;
  }

  function currentHeight(host) {
    var v = host.style.getPropertyValue('--ide-panel-height');
    var n = parseFloat(v);
    if (!isNaN(n) && n > 0) return n;
    var r = host.getBoundingClientRect();
    return Math.round(r.height * 0.3);
  }

  function apply(host, height) {
    var max = Math.round(host.getBoundingClientRect().height * MAX_RATIO);
    var h = Math.max(MIN, Math.min(max, Math.round(height)));
    host.style.setProperty('--ide-panel-height', h + 'px');
    try { localStorage.setItem(KEY, String(h)); } catch (e) { }
    window.xfTerminal && window.xfTerminal.refitAll();
  }

  function restore(host) {
    try {
      var saved = parseFloat(localStorage.getItem(KEY) || '');
      if (!isNaN(saved) && saved >= MIN) host.style.setProperty('--ide-panel-height', saved + 'px');
    } catch (e) { }
  }

  function attach(handleId, hostId) {
    var handle = document.getElementById(handleId);
    var host = document.getElementById(hostId);
    if (!handle || !host) return;
    restore(host);

    handle.addEventListener('pointerdown', function (e) {
      if (e.button !== undefined && e.button !== 0) return;
      e.preventDefault();
      active = { host: host, startY: e.clientY, startH: currentHeight(host) };
      handle.setPointerCapture && handle.setPointerCapture(e.pointerId);
      document.body.classList.add('ide-resizing');
    });

    window.addEventListener('pointermove', function (e) {
      if (!active) return;
      apply(active.host, active.startH + (e.clientY - active.startY));
    });

    window.addEventListener('pointerup', function () {
      if (!active) return;
      active = null;
      document.body.classList.remove('ide-resizing');
      window.xfTerminal && window.xfTerminal.refitAll();
    });

    // duplo clique maximiza/restaura (como o VS Code)
    handle.addEventListener('dblclick', function () {
      var r = host.getBoundingClientRect();
      var max = Math.round(r.height * MAX_RATIO);
      apply(host, currentHeight(host) > MIN + 40 ? MIN : max);
    });
  }

  // ponte Blazor: Ctrl+` (crase) alterna o painel, como no VS Code
  function register(dotNetRef) {
    document.addEventListener('keydown', function (e) {
      if (!(e.ctrlKey || e.metaKey) || e.key !== '`') return;
      if (!document.querySelector('.ide-code-grid')) return;
      dotNetRef.invokeMethodAsync('TogglePanel');
    }, false);
  }

  return { attach: attach, apply: apply, restore: restore, register: register };
})();
