// Pickers nativos para XForge IDE (File System Access API com fallback).
// showDirectoryPicker/showOpenFilePicker existem em Chromium (host Electron + Edge/Chrome);
// fora desse ambiente, usamos fallback com <input type="file" webkitdirectory>.
window.xfPickers = (function () {
  const directorySupported = typeof window.showDirectoryPicker === "function";
  const fileSupported = typeof window.showOpenFilePicker === "function";

  function fallbackFile(callback, multiple, accept) {
    const input = document.createElement("input");
    input.type = "file";
    input.multiple = !!multiple;
    if (accept) input.accept = accept;
    input.style.display = "none";
    document.body.appendChild(input);
    input.addEventListener("change", function () {
      const files = Array.from(input.files || []);
      document.body.removeChild(input);
      if (files.length) {
        callback(files.map(function (f) { return { name: f.name, path: f.path || null }; }));
      }
    });
    input.click();
  }

  function fallbackDirectory(callback) {
    const input = document.createElement("input");
    input.type = "file";
    input.setAttribute("webkitdirectory", "");
    input.style.display = "none";
    document.body.appendChild(input);
    input.addEventListener("change", function () {
      const files = Array.from(input.files || []);
      document.body.removeChild(input);
      if (files.length && files[0].webkitRelativePath) {
        var top = files[0].webkitRelativePath.split("/")[0];
        callback([{ name: top, path: null }]);
      }
    });
    input.click();
  }

  return {
    /** true quando a File System Access API pode selecionar pastas. */
    hasDirectorySupport: function () { return directorySupported; },

    /** true quando a File System Access API pode selecionar arquivos. */
    hasFileSupport: function () { return fileSupported; },

    /** Compatibilidade: true quando há algum seletor nativo. */
    hasNativeSupport: function () { return directorySupported || fileSupported; },

    /** Abre o seletor de pastas. Retorna [{name, path}] ou vazio. */
    pickDirectory: async function () {
      if (!directorySupported) {
        return new Promise(function (resolve) {
          fallbackDirectory(function (r) { resolve(r); });
          setTimeout(function () { resolve([]); }, 60000);
        });
      }
      try {
        var dir = await window.showDirectoryPicker({ mode: "readwrite" });
        // A File System Access API não expõe caminho absoluto por segurança.
        return [{ name: dir.name, path: null }];
      } catch (e) {
        if (e && e.name === "AbortError") return [];
        throw e;
      }
    },

    /** Abre o seletor de arquivos. Retorna [{name, path}] ou vazio. */
    pickFile: async function (accept) {
      if (!fileSupported) {
        return new Promise(function (resolve) {
          fallbackFile(function (r) { resolve(r); }, false, accept || "");
          setTimeout(function () { resolve([]); }, 60000);
        });
      }
      try {
        var handle = await window.showOpenFilePicker({ multiple: false });
        var file = await handle[0].getFile();
        return [{ name: file.name, path: file.path || null }];
      } catch (e) {
        if (e && e.name === "AbortError") return [];
        throw e;
      }
    }
  };
})();
