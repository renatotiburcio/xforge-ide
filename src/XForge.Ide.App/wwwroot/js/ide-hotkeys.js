// Atalhos do editor (padrão VS Code).
// Ctrl+W fecha a aba ativa | Ctrl+Shift+F busca global | Ctrl+P quick open.
(function () {
  // ponte para o Quick Open do Blazor
  var quickOpenRef = null;
  window.xfHotkeys = {
    registerQuickOpen: function (dotNetRef) {
      quickOpenRef = dotNetRef;
      document.addEventListener('ide-quick-open', function () {
        if (quickOpenRef) {
          quickOpenRef.invokeMethodAsync('ShowQuickOpen');
        }
      }, false);
    }
  };

  function activeTabClose() {
    var btn = document.querySelector('.ide-tab--active .ide-tab__close');
    if (btn) { btn.click(); return true; }
    return false;
  }

  function goTo(selector) {
    var link = document.querySelector(selector);
    if (link) { link.click(); return true; }
    return false;
  }

  document.addEventListener('keydown', function (e) {
    var ctrl = e.ctrlKey || e.metaKey;

    // Ctrl+W — fechar aba ativa
    if (ctrl && !e.shiftKey && (e.key === 'w' || e.key === 'W')) {
      if (activeTabClose()) { e.preventDefault(); }
      return;
    }

    // Ctrl+Shift+F — busca global
    if (ctrl && e.shiftKey && (e.key === 'f' || e.key === 'F')) {
      if (goTo('a[href="/code/search"]')) { e.preventDefault(); }
      return;
    }

    // Ctrl+P — quick open (abre a paleta no editor)
    if (ctrl && !e.shiftKey && (e.key === 'p' || e.key === 'P')) {
      if (document.querySelector('.ide-code-grid')) {
        document.dispatchEvent(new CustomEvent('ide-quick-open'));
        e.preventDefault();
      }
      return;
    }

    // F5 — build & run
    if (e.key === 'F5' && !ctrl) {
      if (goTo('a[href="/run"]')) { e.preventDefault(); }
      return;
    }
  }, false);
})();
