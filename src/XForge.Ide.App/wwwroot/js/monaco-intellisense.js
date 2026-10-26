// IntelliSense (Roslyn) para o Monaco: completions, hover e diagnósticos via SignalR.
// Usado pelo MonacoEditor quando há um projeto aberto (arquivos .cs).
(function () {
  const instances = new Map(); // containerId -> { connection, model, providers, disposable }

  function severity(level) {
    switch (level) {
      case 'error': return 8;   // monaco.MarkerSeverity.Error
      case 'warning': return 4; // Warning
      default: return 2;        // Info
    }
  }

  function kindOfMonaco(kind) {
    // Mapeamento simplificado para os CompletionItemKind do Monaco
    const map = {
      method: 1, function: 3, constructor: 4, field: 5, variable: 6, 'static-class': 10,
      class: 7, 'type': 7, struct: 22, interface: 8, enum: 13, namespace: 9,
      property: 10, event: 23, keyword: 14, symbol: 18, operator: 12,
    };
    return map[kind] || 18;
  }

  async function connect(hubPath, containerId) {
    if (!window.signalR) return null;
    const conn = new signalR.HubConnectionBuilder()
      .withUrl(hubPath)
      .withAutomaticReconnect()
      .build();
    try {
      await conn.start();
      return conn;
    } catch (e) {
      return null;
    }
  }

  function registerProvider(containerId, conn, options) {
    if (!window.monaco || !window.monaco.languages) return null;

    const languages = options.languages || ['csharp'];

    const completion = window.monaco.languages.registerCompletionItemProvider(languages, {
      triggerCharacters: ['.', ' '],
      provideCompletionItems: async function (model, position) {
        try {
          const req = {
            projectRoot: options.projectRoot,
            filePath: options.filePath,
            content: model.getValue(),
            line: position.lineNumber,
            column: position.column,
          };
          const items = await conn.invoke('CompletionsAsync', req);
          return {
            suggestions: (items || []).map(function (i) {
              return {
                label: i.label,
                detail: i.detail,
                kind: kindOfMonaco(i.kind),
                insertText: i.insertText || i.label,
                sortText: String(i.sortPriority) + i.label,
                range: null,
              };
            }),
          };
        } catch (e) {
          return { suggestions: [] };
        }
      },
    });

    const hover = window.monaco.languages.registerHoverProvider(languages, {
      provideHover: async function (model, position) {
        try {
          const req = {
            projectRoot: options.projectRoot,
            filePath: options.filePath,
            content: model.getValue(),
            line: position.lineNumber,
            column: position.column,
          };
          const info = await conn.invoke('HoverAsync', req);
          if (!info || !info.contents) return null;
          return { contents: [{ value: info.contents }] };
        } catch (e) {
          return null;
        }
      },
    });

    return { completion: completion, hover: hover };
  }

  // Debounce de diagnósticos por container
  const timers = new Map();

  async function runDiagnostics(containerId, conn, options, editor) {
    if (!editor) return;
    var model = editor.getModel();
    if (!model) return;
    try {
      const req = {
        projectRoot: options.projectRoot,
        filePath: options.filePath,
        content: model.getValue(),
        line: 1,
        column: 1,
      };
      const diags = await conn.invoke('DiagnosticsAsync', req);
      const markers = (diags || []).map(function (d) {
        return {
          startLineNumber: d.line,
          startColumn: d.column,
          endLineNumber: d.endLine,
          endColumn: d.endColumn,
          message: d.message,
          severity: severity(d.severity),
          code: d.id,
          source: 'roslyn',
        };
      });
      window.monaco.editor.setModelMarkers(model, 'roslyn', markers);
    } catch (e) { /* silencioso: intellisense é auxiliar */ }
  }

  window.xfIntellisense = {
    /** Liga o IntelliSense a um editor já criado. */
    async attach(containerId, options) {
      if (!options || !options.projectRoot || !window.monaco) return false;
      if (options.filePath && !/\.cs$/i.test(options.filePath)) return false;

      const existing = instances.get(containerId);
      if (existing) { existing.detach(); instances.delete(containerId); }

      const conn = await connect(options.hubPath || '/hubs/intellisense', containerId);
      if (!conn) return false;

      const providers = registerProvider(containerId, conn, options);
      instances.set(containerId, { conn: conn, options: options, detach: function () {
        if (providers) { providers.completion.dispose(); providers.hover.dispose(); }
      } });

      // pega a instância real do editor criada pelo interop
      var editor = null;
      try {
        editor = window.xfMonaco && window.xfMonaco.getEditor
          ? window.xfMonaco.getEditor(containerId)
          : null;
      } catch (e) { }

      if (editor) {
        var model = editor.getModel();
        if (model) {
          window.monaco.editor.setModelMarkers(model, 'roslyn', []);
        }
        editor.onDidChangeModelContent(function () {
          clearTimeout(timers.get(containerId));
          timers.set(containerId, setTimeout(function () {
            runDiagnostics(containerId, conn, options, editor);
          }, 800));
        });
        runDiagnostics(containerId, conn, options, editor);
      }
      return true;
    },

    /** Invalida o cache do projeto no servidor (após salvar). */
    async invalidate(projectRoot) {
      for (const inst of instances.values()) {
        try { await inst.conn.invoke('InvalidateAsync', projectRoot); } catch (e) { }
      }
    },

    disposeAll: function () {
      for (const inst of instances.values()) { inst.detach(); }
      instances.clear();
    },
  };
})();
