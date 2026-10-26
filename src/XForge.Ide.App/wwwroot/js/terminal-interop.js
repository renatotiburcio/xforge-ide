// Terminal integrado: xterm.js embutido (zero CDN) + SignalR para o ConPTY do servidor.
// Cada painel é identificado por um id único (um terminal do cliente).
window.xfTerminal = (function () {
  const instances = new Map(); // id -> { term, fit, connection, started }

  // DNA visual: tema do xterm composto a partir dos tokens do design system
  function tokenTheme() {
    var st = getComputedStyle(document.documentElement);
    function t(n) { return (st.getPropertyValue('--color-' + n) || '').trim() || null; }
    function hex(v, fb) {
      if (!v) return fb;
      if (v[0] === '#') return v;
      var m = v.match(/-?\d+(\.\d+)?/g);
      if (!m || m.length < 3) return fb;
      return '#' + m.slice(0, 3).map(function (n) {
        var i = Math.round(parseFloat(n));
        return ('0' + Math.max(0, Math.min(255, i)).toString(16)).slice(-2);
      }).join('');
    }
    var dark = document.documentElement.getAttribute('data-theme') === 'dark';
    var bg = hex(t('bg-surface'), dark ? '#0f172a' : '#ffffff');
    var fg = hex(t('text-primary'), dark ? '#e2e8f0' : '#0f172a');
    var primary = hex(t('primary'), '#6366f1');
    var success = hex(t('success'), '#16a34a');
    var warning = hex(t('warning'), '#d97706');
    var danger = hex(t('danger'), '#dc2626');
    var info = hex(t('info'), '#2563eb');
    var muted = hex(t('text-muted'), dark ? '#94a3b8' : '#64748b');
    return {
      background: bg,
      foreground: fg,
      cursor: primary,
      cursorAccent: bg,
      selectionBackground: primary + '55',
      black: muted, red: danger, green: success, yellow: warning,
      blue: info, magenta: primary, cyan: info, white: fg,
      brightBlack: muted, brightRed: danger, brightGreen: success, brightYellow: warning,
      brightBlue: info, brightMagenta: primary, brightCyan: info, brightWhite: fg,
    };
  }

  function signalR() {
    if (!window.signalR) throw new Error('cliente SignalR não carregado');
    return window.signalR;
  }

  // cria o xterm e abre a conexão com o hub
  async function create(containerId, id, hubPath, workingDirectory) {
    if (instances.has(id)) return true;
    const el = document.getElementById(containerId);
    if (!el) return false;

    const term = new window.Terminal({
      fontSize: 13,
      fontFamily: 'JetBrains Mono, ui-monospace, Consolas, "Cascadia Mono", monospace',
      cursorBlink: true,
      scrollback: 5000,
      theme: tokenTheme(),
      allowProposedApi: true,
    });

    const fit = new window.FitAddon.FitAddon();
    term.loadAddon(fit);
    term.open(el);
    fit.fit();

    // forma explícita (evita ambiguidade de precedência de `new a().B()`)
    var builder = signalR().HubConnectionBuilder;
    const conn = new builder()
      .withUrl(hubPath)
      .withAutomaticReconnect()
      .build();

    conn.on('Data', function (data) { term.write(data); });

    let joined = false;
    async function ensureJoined() {
      if (joined || conn.state !== 'Connected') return;
      await conn.invoke('Join', id, workingDirectory);
      joined = true;
      conn.invoke('Resize', id, term.cols, term.rows).catch(function () { });
    }

    conn.onreconnected(function () {
      joined = false;
      ensureJoined().catch(function () { });
    });

    await conn.start();
    await ensureJoined();

    term.onData(function (data) {
      if (conn.state === 'Connected') conn.invoke('Input', id, data).catch(function () { });
    });

    term.onResize(function (size) {
      if (conn.state === 'Connected') conn.invoke('Resize', id, size.cols, size.rows).catch(function () { });
    });

    instances.set(id, { term: term, fit: fit, conn: conn, started: true });
    return true;
  }

  function refit(id) {
    const inst = instances.get(id);
    if (!inst) return;
    try {
      inst.fit.fit();
      const t = inst.term;
      if (inst.conn.state === 'Connected') inst.conn.invoke('Resize', id, t.cols, t.rows).catch(function () { });
    } catch (e) { /* container oculto */ }
  }

  // refaz o fit de todos (chamado ao trocar aba / redimensionar janela)
  function refitAll() {
    for (const id of instances.keys()) refit(id);
  }

  // reaplica o tema dos tokens em todos os terminais abertos
  function applyTokenTheme() {
    var th = tokenTheme();
    for (const inst of instances.values()) {
      try { inst.term.options.theme = th; } catch (e) { }
    }
  }

  function focus(id) {
    const inst = instances.get(id);
    if (inst) { inst.term.focus(); refit(id); }
  }

  function dispose(id) {
    const inst = instances.get(id);
    if (!inst) return;
    instances.delete(id);
    try { inst.conn.stop(); } catch (e) { }
    try { inst.term.dispose(); } catch (e) { }
  }

  window.addEventListener('resize', function () { refitAll(); });

  return { create: create, refit: refit, refitAll: refitAll, focus: focus, dispose: dispose, applyTokenTheme: applyTokenTheme };
})();

// acompanha a troca de tema da IDE (atributo data-theme no <html>)
(function () {
  var html = document.documentElement;
  new MutationObserver(function () {
    window.xfTerminal && window.xfTerminal.applyTokenTheme();
  }).observe(html, { attributes: true, attributeFilter: ['data-theme'] });
})();
