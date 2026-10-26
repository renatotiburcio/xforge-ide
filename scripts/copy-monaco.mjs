// Copia o Monaco Editor (AMD) para o wwwroot do Server — embed total, zero CDN.
import { cp, rm, mkdir } from "node:fs/promises";

const src = "node_modules/monaco-editor/min/vs";
const dest = "src/XForge.Ide.Server/wwwroot/monaco/vs";

await rm(dest, { recursive: true, force: true });
await mkdir(dest, { recursive: true });
await cp(src, dest, { recursive: true });
console.log("Monaco copiado para wwwroot/monaco/vs");
