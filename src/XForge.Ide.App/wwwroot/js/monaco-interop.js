// Monaco Editor interop para XForge IDE (embed AMD, zero CDN).
window.xfMonaco = (function () {
  let loaderPromise = null;
  const editors = new Map();

  // ---------------------------------------------------------------------------
  // DiffEditor: navegação entre diferenças, opções de exibição e reverção.
  // As funções públicas aguardam a criação assíncrona do editor antes de tocar
  // na instância — o componente Blazor chama assim que renderiza.
  // ---------------------------------------------------------------------------
  const diffIndex = new Map();

  /** Verifica se a instância é um DiffEditor (e não um editor comum). */
  const isDiff = (ed) => !!ed && typeof ed.getModifiedEditor === 'function';

  /** Aguarda o DiffEditor existir no mapa (a criação é assíncrona no AMD). */
  async function waitForDiff(containerId) {
    if (isDiff(editors.get(containerId))) return editors.get(containerId);
    for (let i = 0; i < 100; i++) {
      await new Promise((res) => setTimeout(res, 80));
      if (isDiff(editors.get(containerId))) return editors.get(containerId);
    }
    return null;
  }

  /** Diferenças atuais (getLineChanges do DiffEditor) normalizadas. */
  function lineChanges(diff) {
    try {
      const changes = diff.getLineChanges ? diff.getLineChanges() : null;
      return changes || [];
    } catch (e) { return []; }
  }

  /** Intervalo 1-based inclusivo de um lado da diferença. */
  function rangeOf(side) {
    const start = side.startLineNumber;
    const end = Math.max(side.endLineNumber, start - 1);
    return {
      startLineNumber: start,
      endLineNumber: end,
      empty: end < start,
    };
  }

  /** Posiciona uma diferença específica (índice 0-based) nos dois editores. */
  function revealChange(diff, index) {
    const changes = lineChanges(diff);
    const change = changes[index];
    if (!change) return 0;
    const target = diff.getModifiedEditor && diff.getModifiedEditor();
    if (!target) return changes.length;
    const line = Math.max(1, change.modifiedStartLineNumber);
    const endLine = Math.max(line, change.modifiedEndLineNumber >= line ? change.modifiedEndLineNumber : line);
    try {
      target.setSelection({
        startLineNumber: line, startColumn: 1, endLineNumber: endLine, endColumn: 1,
      });
      target.revealLineNearTop(line);
      target.focus();
    } catch (e) { /* diff ainda computando */ }
    return changes.length;
  }

  /** Dispara a ação nativa do Monaco (diffReview.next/prev) quando disponível. */
  function runDiffAction(diff, id) {
    try {
      const ed = diff.getModifiedEditor ? diff.getModifiedEditor() : null;
      const action = ed && ed.getAction ? ed.getAction(id) : null;
      if (action && typeof action.run === 'function') action.run();
      return true;
    } catch (e) { return false; }
  }

  /** Aplica as preferências de exibição do diff (render/overview/minimapa). */
  async function applyDiffOptions(diff, opts) {
    opts = opts || {};
    const sideBySide = opts.inline !== true;
    try {
      diff.updateOptions({
        renderSideBySide: sideBySide,
        ignoreTrimWhitespace: opts.ignoreWhitespace === true,
        hideUnchangedRegions: {
          enabled: opts.onlyDifferences === true,
          contextLineCount: opts.onlyDifferences === true ? 3 : 0,
          minimumLineCount: 3,
          revealLineCount: 20,
        },
        renderOverviewRuler: opts.overview !== false,
        originalEditable: opts.originalEditable === true,
      });
    } catch (e) { /* versão do Monaco sem alguma opção */ }

    const wrap = opts.wordWrap ? 'on' : 'off';
    for (const side of [diff.getOriginalEditor(), diff.getModifiedEditor()]) {
      if (!side || !side.updateOptions) continue;
      try { side.updateOptions({ wordWrap: wrap }); } catch (e) { /* ignora */ }
      if (opts.minimap !== undefined) {
        try { side.updateOptions({ minimap: { enabled: opts.minimap } }); } catch (e) { /* ignora */ }
      }
    }
  }

  /** Texto do lado solicitado da diferença informada (0-based, inclusivo). */
  function linesText(model, start, end) {
    if (!model || start < 1) return [];
    const last = end >= start ? end : start - 1;
    return model.getLinesContent().slice(start - 1, last);
  }

  function load(basePath) {
    if (loaderPromise) return loaderPromise;
    loaderPromise = new Promise((resolve, reject) => {
      const script = document.createElement("script");
      script.src = basePath + "/vs/loader.js";
      script.onload = () => {
        require.config({ paths: { vs: basePath + "/vs" } });
        resolve(window.require);
      };
      script.onerror = () => reject(new Error("Falha ao carregar loader do Monaco"));
      document.head.appendChild(script);
    });
    return loaderPromise;
  }

  /** Modelo efetivo: DiffEditor expõe {original,modified}; editor comum expõe o modelo. */
  function modelOf(ed) {
    var m = ed && ed.getModel ? ed.getModel() : null;
    if (!m) return null;
    return m.modified ? m.modified : m;
  }

  // ---------------------------------------------------------------------------
  // DNA visual: temas do Monaco construídos a partir dos tokens do design system
  // (as mesmas custom properties usadas por todo o resto da IDE).
  // ---------------------------------------------------------------------------
  const TOKEN_NAMES = [
    'bg', 'bg-surface', 'surface-100', 'surface-200', 'border', 'border-light',
    'text-primary', 'text-secondary', 'text-muted', 'text-subtle',
    'primary', 'primary-hover', 'secondary', 'success', 'warning', 'danger', 'info'
  ];

  function readTokens() {
    var st = getComputedStyle(document.documentElement);
    var t = {};
    TOKEN_NAMES.forEach(function (n) {
      var v = st.getPropertyValue('--color-' + n).trim();
      t[n] = v || null;
    });
    return t;
  }

  /** Converte "rgb(99 102 241)" em "#6366f1" (Monaco exige hex ou #rrggbbaa). */
  function toHex(value, fallback) {
    if (!value) return fallback;
    if (value[0] === '#') return value;
    var m = value.match(/-?\d+(\.\d+)?/g);
    if (!m || m.length < 3) return fallback;
    var h = m.slice(0, 3).map(function (n) {
      var i = Math.round(parseFloat(n));
      return ('0' + Math.max(0, Math.min(255, i)).toString(16)).slice(-2);
    }).join('');
    return '#' + h;
  }

  /** Aplica alfa a uma cor hex (#rrggbb + aa). */
  function withAlpha(hex, alpha) {
    if (!hex || hex[0] !== '#') return hex;
    var a = Math.round(Math.max(0, Math.min(1, alpha)) * 255);
    return hex + ('0' + a.toString(16)).slice(-2);
  }

  function buildTheme(kind) {
    var tk = readTokens();
    var light = kind === 'light';
    var def = {
      bg: light ? '#ffffff' : '#0f172a',
      surface: light ? '#ffffff' : '#151c2c',
      surfaceAlt: light ? '#f8fafc' : '#1b2436',
      border: light ? '#e2e8f0' : '#2b3648',
      fg: light ? '#0f172a' : '#e2e8f0',
      fgMuted: light ? '#64748b' : '#94a3b8',
      primary: light ? '#6366f1' : '#a5b4fc',
      success: light ? '#16a34a' : '#4ade80',
      warning: light ? '#d97706' : '#fbbf24',
      danger: light ? '#dc2626' : '#f87171',
      info: light ? '#2563eb' : '#60a5fa',
      keyword: light ? '#7c3aed' : '#c4b5fd',
      type: light ? '#0e7490' : '#67e8f9',
    };
    // tokens reais do design system têm precedência sobre os padrões
    if (tk['bg-surface']) def.surface = toHex(tk['bg-surface'], def.surface);
    if (tk['bg']) def.bg = toHex(tk['bg'], def.bg);
    if (tk['surface-100']) def.surfaceAlt = toHex(tk['surface-100'], def.surfaceAlt);
    if (tk['border']) def.border = toHex(tk['border'], def.border);
    if (tk['text-primary']) def.fg = toHex(tk['text-primary'], def.fg);
    if (tk['text-muted']) def.fgMuted = toHex(tk['text-muted'], def.fgMuted);
    if (tk['text-secondary']) def.fgSecondary = toHex(tk['text-secondary'], def.fgSecondary);
    if (tk['primary']) def.primary = toHex(tk['primary'], def.primary);
    if (tk['success']) def.success = toHex(tk['success'], def.success);
    if (tk['warning']) def.warning = toHex(tk['warning'], def.warning);
    if (tk['danger']) def.danger = toHex(tk['danger'], def.danger);
    if (tk['info']) def.info = toHex(tk['info'], def.info);

    return {
      base: light ? 'vs' : 'vs-dark',
      inherit: true,
      rules: [
        { token: '', foreground: def.fg.slice(1), background: def.surface.slice(1) },
        { token: 'comment', foreground: def.fgMuted.slice(1), fontStyle: 'italic' },
        { token: 'keyword', foreground: def.keyword.slice(1) },
        { token: 'keyword.operator', foreground: def.fg.slice(1) },
        { token: 'string', foreground: def.success.slice(1) },
        { token: 'string.escape', foreground: def.warning.slice(1) },
        { token: 'number', foreground: def.warning.slice(1) },
        { token: 'constant', foreground: def.info.slice(1) },
        { token: 'type', foreground: def.type.slice(1) },
        { token: 'type.identifier', foreground: def.type.slice(1) },
        { token: 'identifier', foreground: def.fg.slice(1) },
        { token: 'variable', foreground: def.fg.slice(1) },
        { token: 'variable.predefined', foreground: def.info.slice(1) },
        { token: 'function', foreground: def.primary.slice(1) },
        { token: 'attribute.name', foreground: def.type.slice(1) },
        { token: 'attribute.value', foreground: def.success.slice(1) },
        { token: 'tag', foreground: def.primary.slice(1) },
        { token: 'delimiter', foreground: def.fgMuted.slice(1) },
        { token: 'comment.doc', foreground: def.fgMuted.slice(1), fontStyle: 'italic' },
      ],
      colors: {
        'editor.background': def.surface,
        'editor.foreground': def.fg,
        'editorLineNumber.foreground': def.fgMuted,
        'editorLineNumber.activeForeground': def.primary,
        'editorCursor.foreground': def.primary,
        'editor.selectionBackground': withAlpha(def.primary, 0.22),
        'editor.inactiveSelectionBackground': withAlpha(def.primary, 0.12),
        'editor.lineHighlightBackground': def.surfaceAlt,
        'editor.lineHighlightBorder': '#00000000',
        'editorIndentGuide.background': def.border,
        'editorIndentGuide.activeBackground': def.primary,
        'editorWidget.background': def.surfaceAlt,
        'editorWidget.border': def.border,
        'editorSuggestWidget.background': def.surfaceAlt,
        'editorSuggestWidget.border': def.border,
        'editorSuggestWidget.foreground': def.fg,
        'editorSuggestWidget.selectedBackground': withAlpha(def.primary, 0.18),
        'editorHoverWidget.background': def.surfaceAlt,
        'editorHoverWidget.border': def.border,
        'scrollbarSlider.background': withAlpha(def.fgMuted, 0.3),
        'scrollbarSlider.hoverBackground': withAlpha(def.fgMuted, 0.45),
        'scrollbarSlider.activeBackground': withAlpha(def.fgMuted, 0.6),
        'minimap.background': def.surfaceAlt,
      },
    };
  }

  /** Registra (ou redefine) os temas xforge-light / xforge-dark e aplica o pedido. */
  function defineTheme(kind) {
    if (!window.monaco || !window.monaco.editor) return false;
    var name = 'xforge-' + kind;
    window.monaco.editor.defineTheme(name, buildTheme(kind));
    window.monaco.editor.setTheme(name);
    return true;
  }

  return {
    /** Cria (ou recria) um editor no elemento informado. */
    async create(containerId, dotNetRef, options) {
      return await createWithTheme(containerId, dotNetRef, options, options.theme);
    },

    /** Cria o editor já com um tema do design system definido (xforge-light/dark). */
    async createWithTheme(containerId, dotNetRef, options, kind) {
      const require = await load(options.basePath);
      await new Promise((res) => require(["vs/editor/editor.main"], res));
      // só depois do Monaco carregado: define o tema dos tokens e aplica
      try { defineTheme(kind); } catch (e) { /* tema padrão do Monaco */ }

      const container = document.getElementById(containerId);
      if (!container) throw new Error("Container não encontrado: " + containerId);

      const existing = editors.get(containerId);
      if (existing) existing.dispose();

      const editor = window.monaco.editor.create(container, {
        value: options.value || "",
        language: options.language || "plaintext",
        theme: options.theme || "vs-dark",
        automaticLayout: true,
        fontSize: 13,
        fontFamily: "'JetBrains Mono', ui-monospace, monospace",
        minimap: { enabled: options.minimap !== false },
        bracketPairColorization: { enabled: true },
        stickyScroll: { enabled: true },
        formatOnPaste: true,
        formatOnType: true,
        renderLineHighlight: "all",
        scrollBeyondLastLine: false,
        wordWrap: options.wordWrap ? "on" : "off",
      });

      editor.onDidChangeModelContent(() => {
        dotNetRef.invokeMethodAsync("OnContentChanged", editor.getValue());
      });

      editor.addCommand(window.monaco.KeyMod.CtrlCmd | window.monaco.KeyCode.KeyS, () => {
        dotNetRef.invokeMethodAsync("OnSaveRequested", editor.getValue());
      });

      editors.set(containerId, editor);
      return editor.getValue();
    },

    /** Cria um DiffEditor (com tema do design system). */
    async createDiff(containerId, dotNetRef, options, kind) {
      const require = await load(options.basePath);
      await new Promise((res) => require(["vs/editor/editor.main"], res));
      try { defineTheme(kind || (options.theme === 'xforge-dark' ? 'dark' : 'light')); } catch (e) { }
      await new Promise((res) => require(["vs/editor/editor.main"], res));
      const container = document.getElementById(containerId);
      if (!container) throw new Error("Container não encontrado: " + containerId);
      const existing = editors.get(containerId);
      if (existing) disposeDiff(existing);
      const diff = window.monaco.editor.createDiffEditor(container, {
        theme: options.theme || "vs-dark",
        automaticLayout: true,
        renderSideBySide: true,
      });
      diff.setModel({
        original: window.monaco.editor.createModel(options.original || "", options.language || "plaintext"),
        // o componente passa o lado modificado em "value" (options.modified é opcional)
        modified: window.monaco.editor.createModel(options.modified || options.value || "", options.language || "plaintext"),
      });
      editors.set(containerId, diff);
      diffIndex.set(containerId, 0);
      return true;
    },

    // --------------------------------------------------------------- diff (M3)
    /** Aplica as opções de exibição do diff (retorna o estado atual). */
    async diffOptions(containerId, opts) {
      const diff = await waitForDiff(containerId);
      if (!diff) return null;
      await applyDiffOptions(diff, opts);
      return this.diffState(containerId);
    },

    /** Contagem de diferenças e índice atual (base do contador "3 de 12"). */
    async diffState(containerId) {
      const diff = await waitForDiff(containerId);
      if (!diff) return null;
      const changes = lineChanges(diff);
      return { count: changes.length, index: diffIndex.get(containerId) || 0 };
    },

    /** Volta o índice para a primeira diferença. */
    diffReset(containerId) {
      diffIndex.set(containerId, 0);
      return this.diffState(containerId);
    },

    /** Próxima diferença (ação nativa do Monaco + posicionamento). */
    async diffNext(containerId) {
      const diff = await waitForDiff(containerId);
      if (!diff) return null;
      const changes = lineChanges(diff);
      if (changes.length === 0) return { count: 0, index: -1 };
      const index = Math.min(changes.length - 1, (diffIndex.get(containerId) || 0) + 1);
      diffIndex.set(containerId, index);
      revealChange(diff, index);
      runDiffAction(diff, 'editor.action.diffReview.next');
      return { count: changes.length, index: index };
    },

    /** Diferença anterior (ação nativa do Monaco + posicionamento). */
    async diffPrev(containerId) {
      const diff = await waitForDiff(containerId);
      if (!diff) return null;
      const changes = lineChanges(diff);
      if (changes.length === 0) return { count: 0, index: -1 };
      const index = Math.max(0, (diffIndex.get(containerId) || 0) - 1);
      diffIndex.set(containerId, index);
      revealChange(diff, index);
      runDiffAction(diff, 'editor.action.diffReview.prev');
      return { count: changes.length, index: index };
    },

    /** Vai direto para uma diferença (índice 0-based) — usado pelo "primeira". */
    async diffGoto(containerId, index) {
      const diff = await waitForDiff(containerId);
      if (!diff) return null;
      const changes = lineChanges(diff);
      if (changes.length === 0) return { count: 0, index: -1 };
      const target = Math.max(0, Math.min(changes.length - 1, index | 0));
      diffIndex.set(containerId, target);
      revealChange(diff, target);
      return { count: changes.length, index: target };
    },

    /** Texto selecionado no lado modificado ou no original do diff. */
    async diffSelectedText(containerId) {
      const diff = await waitForDiff(containerId);
      if (!diff) return null;
      const sides = [diff.getModifiedEditor && diff.getModifiedEditor(), diff.getOriginalEditor && diff.getOriginalEditor()];
      for (const side of sides) {
        if (!side || !side.getSelection || !side.getModel) continue;
        const sel = side.getSelection();
        if (sel && !sel.isEmpty()) return side.getModel().getValueInRange(sel);
      }
      return null;
    },

    /**
     * Diferença atual (para o modal de "reverter trecho"): devolve o intervalo
     * dos dois lados e como ficaria o ORIGINAL com o trecho do MODIFICADO.
     * Não altera nada — a escrita no arquivo é decidida pelo usuário.
     */
    async diffRevertPreview(containerId) {
      const diff = await waitForDiff(containerId);
      if (!diff) return null;
      const changes = lineChanges(diff);
      if (changes.length === 0) return null;
      const index = Math.max(0, Math.min(changes.length - 1, diffIndex.get(containerId) || 0));
      const change = changes[index];
      const original = diff.getOriginalEditor && diff.getOriginalEditor();
      const modified = diff.getModifiedEditor && diff.getModifiedEditor();
      if (!original || !modified || !original.getModel || !modified.getModel) return null;
      const origModel = original.getModel();
      const modModel = modified.getModel();
      const origin = rangeOf({ startLineNumber: change.originalStartLineNumber, endLineNumber: change.originalEndLineNumber });
      const target = rangeOf({ startLineNumber: change.modifiedStartLineNumber, endLineNumber: change.modifiedEndLineNumber });
      const modifiedLines = linesText(modModel, target.startLineNumber, target.endLineNumber);
      const origLines = origModel.getLinesContent();
      // remove as linhas originais do trecho e coloca as do modificado no lugar
      const head = origLines.slice(0, Math.max(0, origin.startLineNumber - 1));
      const tail = origLines.slice(Math.max(0, origin.endLineNumber));
      const merged = head.concat(modifiedLines, tail);
      const eol = origModel.getEOL ? origModel.getEOL() : '\n';
      return {
        count: changes.length,
        index: index,
        originalStartLineNumber: origin.startLineNumber,
        originalEndLineNumber: origin.endLineNumber,
        modifiedStartLineNumber: target.startLineNumber,
        modifiedEndLineNumber: target.endLineNumber,
        original: merged.join(eol),
      };
    },

    /** Aplica no modelo original o texto devolvido por diffRevertPreview. */
    async diffRevertApply(containerId, text) {
      const diff = await waitForDiff(containerId);
      if (!diff) return false;
      const original = diff.getOriginalEditor && diff.getOriginalEditor();
      const model = original && original.getModel ? original.getModel() : null;
      if (!model) return false;
      model.setValue(text || '');
      return true;
    },

    /** Modelo efetivo: DiffEditor expõe {original,modified}; editor comum expõe o modelo. */
    setValue(containerId, value) {
      const ed = editors.get(containerId);
      if (!ed) return false;
      if (ed.setValue) { ed.setValue(value); return true; }
      var m = modelOf(ed);
      if (m) { m.setValue(value); return true; }
      return false;
    },

    getValue(containerId) {
      const ed = editors.get(containerId);
      if (!ed) return null;
      if (ed.getValue && !ed.getModel) return ed.getValue();
      var m = modelOf(ed);
      return m ? m.getValue() : null;
    },

    /** Texto selecionado no editor (string vazia se não houver seleção). */
    getSelectedText(containerId) {
      const ed = editors.get(containerId);
      if (!ed) return null;
      var model = modelOf(ed);
      if (!model) return null;
      var sel = ed.getSelection ? ed.getSelection() : null;
      if (!sel || sel.isEmpty()) return null;
      return model.getValueInRange(sel);
    },

    setTheme(theme) {
      window.monaco && window.monaco.editor.setTheme(theme);
    },

    setLanguage(containerId, language) {
      const ed = editors.get(containerId);
      const model = ed && ed.getModel ? ed.getModel() : null;
      if (model) { if (model.modified) { window.monaco.editor.setModelLanguage(model.modified, language); } else { window.monaco.editor.setModelLanguage(model, language); } }
    },

    /** Idioma atual do modelo (debug/verificação da troca de aba). */
    getLanguage(containerId) {
      const ed = editors.get(containerId);
      if (!ed) return null;
      var model = ed.getModel ? ed.getModel() : null;
      if (model && model.modified) model = model.modified;
      return model ? model.getLanguageId() : null;
    },

    layout(containerId) {
      const ed = editors.get(containerId);
      if (ed && ed.layout) ed.layout();
    },

    /** Posiciona o cursor e rola até a linha (1-based). */
    revealLine(containerId, line) {
      const ed = editors.get(containerId);
      if (!ed || !ed.revealLine) return false;
      const ln = Math.max(1, line | 0);
      ed.revealLine(ln);
      ed.setPosition({ lineNumber: ln, column: 1 });
      ed.focus();
      return true;
    },

    /** Define e aplica o tema construído a partir dos tokens do design system. */
    defineTheme(kind) { return defineTheme(kind); },

    /** Aplica preferências do editor (fonte, tab, wrap, minimapa, números de linha). */
    applyPreferences(containerId, prefs) {
      const ed = editors.get(containerId);
      if (!ed || !ed.updateOptions) return false;
      ed.updateOptions({
        fontSize: Number(prefs.fontSize) || 13,
        fontFamily: prefs.fontFamily || "'JetBrains Mono', ui-monospace, monospace",
        tabSize: Number(prefs.tabSize) || 4,
        wordWrap: prefs.wordWrap ? 'on' : 'off',
        minimap: { enabled: prefs.minimap !== false },
        lineNumbers: prefs.lineNumbers === false ? 'off' : 'on',
      });
      return true;
    },

    /** Posiciona o cursor no início do arquivo e rola (usado pelo Quick Open). */
    focusEditor(containerId) {
      const ed = editors.get(containerId);
      if (!ed) return false;
      ed.focus();
      return true;
    },

    /** Instância do editor criada (para o provedor de IntelliSense). */
    getEditor(containerId) { return editors.get(containerId) || null; },

    dispose(containerId) {
      const ed = editors.get(containerId);
      if (ed) { ed.dispose && ed.dispose(); editors.delete(containerId); }
      diffIndex.delete(containerId);
    },

    disposeDiff(ed) { if (ed && ed.dispose) ed.dispose(); },
  };
})();
