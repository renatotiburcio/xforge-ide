// Reposicionador universal de popups do XForge Design System.
// Problema: dropdowns/listboxes/menus são renderizados inline; dentro de modais
// (overflow auto + backdrop-filter) ficam cortados.
// Solução: garantir position:fixed (escapando do overflow) + posição dinâmica
// calculada do trigger, com flip (abrir para cima) e clamp nas bordas da viewport.
window.xfPopup = (function () {
  // [role=tree] NÃO entra: o XFMenu vertical usa role=tree para o menu da
  // sidebar (by design do design system) — não é popup. Dropdowns usam menubar.
  //
  // ATENÇÃO: [role=listbox] NU (descoberto em 26/10/2026) é populado por listas
  // do próprio app (Quick View do dicionário, paleta Quick Open) que NÃO são
  // popups — recebiam position:fixed e eram reposicionadas como dropdown,
  // quebrando o layout. O seletor agora é restrito às raízes dos componentes
  // de formulário do design system (.xf-transfer-list tem listas ESTÁTICAS e
  // portanto fica fora de propósito).
  const DS_FORM_ROOTS = '.xf-select, .xf-combobox, .xf-autocomplete, .xf-multiselect, .xf-dropdown-datagrid';
  const POPUP_SELECTOR = [
    '.xf-select [role=listbox]',
    '.xf-combobox [role=listbox]',
    '.xf-autocomplete [role=listbox]',
    '.xf-multiselect [role=listbox]',
    '.xf-dropdown-datagrid [role=listbox]',
    '.xf-select-dropdown',
    '[role=menu]',
    '.xf-dropdown',
    '.xf-calendar',
    '.xf-menu-panel'
  ].join(', ');
  const TRIGGER_SELECTOR = '[aria-haspopup], [aria-controls], .xf-select__control, .xf-combobox__control, .xf-input';
  const observed = new WeakSet();
  let repositioning = false;

  // O atalho Ctrl+W é tratado uma única vez em ide-hotkeys.js (contrato do TabBar).

  function isVisible(el) { return !!(el.offsetWidth || el.offsetHeight || el.getClientRects().length); }

  // encontra o trigger: elemento com aria-controls para este popup, senão o ancestral mais próximo
  function findTrigger(popup) {
    var id = popup.id;
    if (id) {
      var byAria = document.querySelector('[aria-controls="' + id + '"]');
      if (byAria) return byAria;
      var hasPopup = document.querySelector('[aria-haspopup="listbox"][aria-controls]');
      if (hasPopup && hasPopup.getAttribute('aria-controls') === id) return hasPopup;
    }
    var root = popup.closest(DS_FORM_ROOTS + ', .xf-datepicker');
    if (root) {
      var ctrl = root.querySelector(TRIGGER_SELECTOR) || root.querySelector('input, button');
      if (ctrl) return ctrl;
    }
    return popup.parentElement;
  }

  function position(popup) {
    if (!isVisible(popup)) return;
    var trigger = findTrigger(popup);
    if (!trigger) return;
    var t = trigger.getBoundingClientRect();
    var pw = popup.offsetWidth;
    var ph = popup.offsetHeight;
    var vw = window.innerWidth;
    var vh = window.innerHeight;
    const GAP = 4;
    const EDGE = 8;

    // largura: SEMPRE a do trigger (o popup pode ter sido esticado pelo grid do modal)
    var width = Math.min(Math.max(t.width, 140), vw - 2 * EDGE);
    popup.style.width = width + 'px';
    popup.style.maxWidth = (vw - 2 * EDGE) + 'px';
    ph = popup.offsetHeight;

    // altura máxima: espaço disponível (mínimo 120px)
    var spaceBelow = vh - t.bottom - GAP - EDGE;
    var spaceAbove = t.top - GAP - EDGE;
    var maxH = Math.max(120, Math.min(280, Math.max(spaceBelow, spaceAbove)));
    popup.style.maxHeight = maxH + 'px';
    popup.style.overflowY = 'auto';
    ph = Math.min(ph, maxH);

    // posição vertical: abaixo; se não couber, acima (flip)
    var top;
    if (ph <= spaceBelow && spaceBelow >= 120) {
      top = t.bottom + GAP;
    } else if (ph <= spaceAbove) {
      top = t.top - ph - GAP;
    } else {
      // não cabe dos dois lados: usa o lado maior e limita altura
      top = spaceBelow >= spaceAbove ? t.bottom + GAP : Math.max(EDGE, t.top - maxH - GAP);
      popup.style.maxHeight = Math.max(120, (spaceBelow >= spaceAbove ? spaceBelow : spaceAbove)) + 'px';
    }

    // posição horizontal: alinha ao trigger, com clamp
    var left = t.left;
    if (left + width > vw - EDGE) left = vw - width - EDGE;
    if (left < EDGE) left = EDGE;

    Object.assign(popup.style, {
      position: 'fixed',
      top: top + 'px',
      left: left + 'px',
      zIndex: '2147483000'
    });
  }

  function refresh() {
    if (repositioning) return;
    repositioning = true;
    requestAnimationFrame(function () {
      repositioning = false;
      document.querySelectorAll(POPUP_SELECTOR).forEach(function (p) {
        if (isVisible(p)) position(p);
      });
    });
  }

  function init() {
    if (document.getElementById('xf-popup-style')) return;
    var st = document.createElement('style');
    st.id = 'xf-popup-style';
    st.textContent = [
      // modais/diálogos: remover containing blocks e clipping dos popups
      // (restrito aos componentes de formulário do DS — listas do app ficam de fora)
      '[role="dialog"] .xf-select [role="listbox"], [role="dialog"] .xf-combobox [role="listbox"],' +
        ' [role="dialog"] .xf-autocomplete [role="listbox"], [role="dialog"] .xf-multiselect [role="listbox"],' +
        ' [role="dialog"] .xf-dropdown-datagrid [role="listbox"], [role="dialog"] [role="menu"]' +
        ' { position: fixed !important; }',
      '.xf-select [role="listbox"], .xf-combobox [role="listbox"], .xf-autocomplete [role="listbox"],' +
        ' .xf-multiselect [role="listbox"], .xf-dropdown-datagrid [role="listbox"],' +
        ' .xf-select-dropdown { position: fixed; }'
    ].join('\n');
    document.head.appendChild(st);

    var obs = new MutationObserver(function (muts) {
      var need = false;
      for (var i = 0; i < muts.length; i++) {
        var m = muts[i];
        for (var j = 0; j < m.addedNodes.length; j++) {
          var n = m.addedNodes[j];
          if (n.nodeType === 1 && (n.matches(POPUP_SELECTOR) || (n.querySelector && n.querySelector(POPUP_SELECTOR)))) { need = true; break; }
        }
        if (m.target && m.target.matches && m.target.matches('[aria-expanded]')) need = true;
        if (need) break;
      }
      if (need) refresh();
    });
    obs.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['style', 'class', 'aria-expanded'] });

    window.addEventListener('scroll', refresh, true);
    window.addEventListener('resize', refresh);
    refresh();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

  return { refresh: refresh };
})();
