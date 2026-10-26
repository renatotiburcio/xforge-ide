// Copia o xterm.js (+ addon-fit) e o cliente SignalR do node_modules para o
// wwwroot do projeto App (RCL), mantendo tudo offline (zero CDN), igual ao Monaco.
import { copyFile, mkdir } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, '..');
const nm = join(root, 'node_modules');
// mesmo destino do Monaco (wwwroot do servidor hospedeiro)
const dest = join(root, 'src', 'XForge.Ide.Server', 'wwwroot', 'xterm');

// [origem relativa ao node_modules, destino relativa a wwwroot/xterm]
const files = [
  ['@xterm/xterm/lib/xterm.js', 'xterm.js'],
  ['@xterm/xterm/lib/xterm.js.map', 'xterm.js.map'],
  ['@xterm/xterm/css/xterm.css', 'xterm.css'],
  ['@xterm/addon-fit/lib/addon-fit.js', 'addon-fit.js'],
  ['@microsoft/signalr/dist/browser/signalr.min.js', 'signalr.min.js'],
];

await mkdir(join(dest, 'js'), { recursive: true });
for (const [from, to] of files) {
  await copyFile(join(nm, from), join(dest, 'js', to));
  console.log('copiado', to);
}
console.log('xterm + signalr prontos em wwwroot/xterm/js');
