#!/usr/bin/env node
// Audit automatizado de UI/acessibilidade da XForge IDE.
//
// Estratégia: abre um navegador headless via Chrome DevTools Protocol (CDP),
// navega por todas as rotas Razor da IDE, mede H1, botões sem nome acessível,
// erros de console e exercita modais/diálogos com ações não destrutivas.
//
// Uso:
//   IDE_URL=http://127.0.0.1:5600 node scripts/qa-ui-a11y.mjs
//
// Sem dependências externas além de `ws` (já usado pelo pipeline xterm/SignalR).

import { spawn } from 'node:child_process';
import http from 'node:http';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import net from 'node:net';
import { fileURLToPath } from 'node:url';
import WebSocket from 'ws';

const baseUrl = process.env.IDE_URL || 'http://127.0.0.1:5600';
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const profileDir = path.join(os.tmpdir(), 'xforge-qa-chrome-profile');

// Diretório de solução usado pelo gate: precisa existir antes de criar o projeto
// (ProjectService.CreateAsync recusa caminhos inexistentes).
const projectFixtureDir = path.join(os.tmpdir(), 'xforge-qa-gate-project');
fs.rmSync(projectFixtureDir, { recursive: true, force: true });
fs.mkdirSync(projectFixtureDir, { recursive: true });

const routes = [
  '/', '/home', '/projects', '/models', '/data', '/data/import', '/data/er',
  '/data/migrations', '/code', '/code/search', '/code/diff', '/run', '/run/console',
  '/settings', '/settings/theme', '/settings/ai', '/wizards', '/ai', '/ai/agents',
  '/ai/skills', '/ai/history', '/design', '/design/toolbox', '/design/properties',
  '/git', '/git/log', '/git/branches', '/git/prs', '/settings/github',
  '/wizards/crud', '/wizards/master-detail', '/wizards/grid-form',
  '/wizards/inline-edit', '/wizards/pivot', '/wizards/layouts',
  '/not-found', '/error'
];

const interactions = [
  {
    route: '/projects',
    name: 'Prepara projeto aberto para os testes de dados',
    ensureProject: true,
    steps: []
  },
  {
    route: '/data',
    name: 'Prepara dicionário de teste quando necessário',
    ensureDictionary: true,
    steps: []
  },
  {
    route: '/projects',
    name: 'Modal de novo projeto bloqueia campos vazios',
    steps: [
      { click: 'Novo projeto' },
      { waitText: 'Criar projeto' },
      { expectFocusText: 'Cancelar' },
      { click: 'Criar projeto' },
      { waitText: 'Informe o nome do projeto.' },
      { expectLiveText: 'Informe o nome do projeto.' },
      { click: 'Cancelar' },
      { expectNoAlert: true }
    ]
  },
  {
    route: '/projects',
    name: 'Exclusão de projeto exige confirmação',
    steps: [
      { clickFirst: 'button[aria-label^="Remover "]' },
      { waitText: 'Excluir projeto' },
      { expectFocusText: 'Cancelar' },
      { expectAlertText: 'Excluir projeto' },
      { clickPoint: { x: 8, y: 8 } },
      { expectAlertText: 'Excluir projeto' },
      { click: 'Cancelar' },
      { expectNoAlert: true }
    ]
  },
  {
    route: '/settings',
    name: 'Restaurar padrão exige confirmação',
    steps: [
      { click: 'Restaurar padrão' },
      { waitText: 'Restaurar preferências do editor?' },
      { expectFocusText: 'Cancelar' },
      { clickPoint: { x: 8, y: 8 } },
      { waitText: 'Restaurar preferências do editor?' },
      { click: 'Cancelar' },
      { expectNoAlert: true }
    ]
  },
  {
    route: '/data/er',
    name: 'Reset de layout ER exige confirmação',
    steps: [
      { clickFirst: 'button[aria-label="Refazer layout automático"]' },
      { waitText: 'Refazer layout automático?' },
      { expectFocusText: 'Cancelar' },
      { clickPoint: { x: 8, y: 8 } },
      { waitText: 'Refazer layout automático?' },
      { click: 'Cancelar' },
      { expectNoAlert: true }
    ]
  },
  {
    route: '/data',
    name: 'Exclusão de dicionário exibe confirmação',
    steps: [
      { waitText: 'Novo dicionário' },
      { clickFirst: 'button[aria-label^="Excluir dicionário "]' },
      { waitText: 'Excluir dicionário' },
      { expectFocusText: 'Cancelar' },
      { expectAlertText: 'Excluir dicionário' },
      { clickPoint: { x: 8, y: 8 } },
      { expectAlertText: 'Excluir dicionário' },
      { click: 'Cancelar' },
      { expectNoAlert: true }
    ]
  }
];

function sleep(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

function getFreePort() {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.unref();
    server.on('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const { port } = server.address();
      server.close(() => resolve(port));
    });
  });
}

function httpGetJson(url, timeoutMs = 30000) {
  return new Promise((resolve, reject) => {
    const request = http.get(url, response => {
      let body = '';
      response.setEncoding('utf8');
      response.on('data', chunk => { body += chunk; });
      response.on('end', () => {
        try {
          resolve(JSON.parse(body));
        } catch (error) {
          reject(new Error(`Resposta inválida de ${url}: ${error.message}`));
        }
      });
    });
    request.setTimeout(timeoutMs, () => {
      request.destroy(new Error(`Timeout ao consultar ${url}`));
    });
    request.on('error', reject);
  });
}

function findBrowserPath() {
  const candidates = [
    process.env.CHROME_PATH,
    'C:/Program Files/Google/Chrome/Application/chrome.exe',
    'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
    'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
    'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'
  ].filter(Boolean);

  const found = candidates.find(candidate => fs.existsSync(candidate));
  if (!found) {
    throw new Error('Navegador Chrome/Edge não encontrado. Defina CHROME_PATH com o executável.');
  }
  return found;
}

class CdpClient {
  constructor(ws) {
    this.ws = ws;
    this.nextId = 1;
    this.pending = new Map();
    this.listeners = [];

    this.ws.on('message', raw => {
      const message = JSON.parse(raw.toString());
      if (message.id && this.pending.has(message.id)) {
        const { resolve, reject } = this.pending.get(message.id);
        this.pending.delete(message.id);
        if (message.error) {
          reject(new Error(`${message.error.message} (code ${message.error.code})`));
        } else {
          resolve(message.result);
        }
        return;
      }

      for (const listener of this.listeners) {
        listener(message);
      }
    });
  }

  onMessage(listener) {
    this.listeners.push(listener);
  }

  send(method, params = {}, sessionId) {
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      const payload = JSON.stringify({ id, method, params, sessionId });
      this.ws.send(payload);
      setTimeout(() => {
        if (this.pending.has(id)) {
          this.pending.delete(id);
          reject(new Error(`Timeout CDP: ${method}`));
        }
      }, 30000);
    });
  }
}

const browserPath = findBrowserPath();
const debugPort = await getFreePort();
console.error('[qa] browser=' + browserPath + ' port=' + debugPort);
fs.rmSync(profileDir, { recursive: true, force: true });
fs.mkdirSync(profileDir, { recursive: true });

const browser = spawn(browserPath, [
  '--headless=new',
  `--remote-debugging-port=${debugPort}`,
  `--user-data-dir=${profileDir}`,
  '--no-first-run',
  '--no-default-browser-check',
  '--disable-extensions',
  '--disable-gpu',
  '--window-size=1600,1000',
  'about:blank'
], { stdio: 'ignore' });

let browserClosed = false;
const browserExit = new Promise(resolve => browser.on('exit', () => { browserClosed = true; resolve(); }));

async function waitForDebugger() {
  for (let attempt = 0; attempt < 60; attempt++) {
    if (browserClosed) throw new Error('Navegador foi encerrado antes de iniciar o CDP.');
    try {
      const version = await httpGetJson(`http://127.0.0.1:${debugPort}/json/version`);
      return version.webSocketDebuggerUrl;
    } catch {
      await sleep(250);
    }
  }
  throw new Error('Não foi possível conectar ao endpoint de depuração do navegador.');
}

const debuggerUrl = await waitForDebugger();
const ws = new WebSocket(debuggerUrl, { perMessageDeflate: false, maxPayload: 256 * 1024 * 1024 });
await new Promise((resolve, reject) => {
  ws.once('open', resolve);
  ws.once('error', reject);
});

const cdp = new CdpClient(ws);
const { targetId } = await cdp.send('Target.createTarget', { url: 'about:blank' });
const { sessionId } = await cdp.send('Target.attachToTarget', { targetId, flatten: true });

await cdp.send('Page.enable', {}, sessionId);
await cdp.send('Runtime.enable', {}, sessionId);
await cdp.send('Log.enable', {}, sessionId);
await cdp.send('Input.enable', {}, sessionId).catch(() => {});
await cdp.send('Target.activateTarget', { targetId }).catch(() => {});
await cdp.send('Page.bringToFront', {}, sessionId).catch(() => {});

let consoleErrors = [];
let pageErrors = [];

cdp.onMessage(message => {
  if (message.sessionId && message.sessionId !== sessionId) return;

  if (message.method === 'Runtime.consoleAPICalled' && message.params?.type === 'error') {
    const text = (message.params.args || [])
      .map(arg => arg.value ?? arg.description ?? arg.type)
      .join(' ');
    consoleErrors.push(text.slice(0, 300));
  }

  if (message.method === 'Runtime.exceptionThrown') {
    const details = message.params.exceptionDetails;
    pageErrors.push((details?.exception?.description || details?.text || 'pageerror').slice(0, 300));
  }
});

async function evaluate(expression) {
  const result = await cdp.send('Runtime.evaluate', {
    expression,
    returnByValue: true,
    awaitPromise: false
  }, sessionId);

  if (result.exceptionDetails) {
    throw new Error(result.exceptionDetails.exception?.description || result.exceptionDetails.text);
  }
  return result.result.value;
}

const helpers = `
window.__qaClick = (selector) => {
  const element = document.querySelector(selector);
  if (!element) return false;
  element.click();
  return true;
};
window.__qaClickText = (text) => {
  const element = Array.from(document.querySelectorAll('button, a')).find(el => (el.textContent || '').trim() === text);
  if (!element) return false;
  element.click();
  return true;
};
window.__qaClickLabel = (label) => {
  const element = Array.from(document.querySelectorAll('button, a, [role="treeitem"], [role="tab"]'))
    .find(el => (el.getAttribute('aria-label') || el.getAttribute('title') || '').trim() === label);
  if (!element) return false;
  element.click();
  return true;
};
`;

async function gotoRoute(route) {
  consoleErrors = [];
  pageErrors = [];

  await cdp.send('Page.navigate', { url: baseUrl + route }, sessionId);

  for (let attempt = 0; attempt < 100; attempt++) {
    const state = await evaluate(`({
      url: location.href,
      ready: document.readyState,
      blazor: typeof window.Blazor !== 'undefined',
      buttons: document.querySelectorAll('button').length
    })`);
    if (state?.ready === 'complete' && state.blazor === true && state.buttons > 0) break;
    await sleep(100);
  }

  await sleep(1800);
  return evaluate(helpers + `(() => {
    const main = document.querySelector('main')?.innerText || document.body.innerText;
    const controls = Array.from(document.querySelectorAll('button, [role=button], a'));
    const unlabeled = controls.filter(el => {
      const label = (el.getAttribute('aria-label') || el.textContent || el.getAttribute('title') || '').trim();
      return !label;
    }).length;

    return {
      title: document.title,
      h1: document.querySelectorAll('h1').length,
      firstHeading: document.querySelector('h1,h2,h3,h4,h5,h6')?.tagName || null,
      unlabeledControls: unlabeled,
      duplicateTableLabels: (() => {
        const tableControls = Array.from(document.querySelectorAll('table button, table [role=button]'));
        const counts = new Map();
        for (const el of tableControls) {
          const label = (el.getAttribute('aria-label') || el.textContent || el.getAttribute('title') || '').trim();
          if (!label) continue;
          counts.set(label, (counts.get(label) || 0) + 1);
        }
        return Array.from(counts.values()).filter(count => count > 1).length;
      })(),
      replacementChars: main.includes('\\uFFFD'),
      tabsWithoutRole: document.querySelectorAll('.ide-panel__tabs button:not([role=tab])').length
    };
  })()`);
}

async function waitForText(text, timeoutMs = 8000) {
  const expression = `document.body.innerText.includes(${JSON.stringify(text)})`;
  for (let attempt = 0; attempt < timeoutMs / 100; attempt++) {
    if (await evaluate(expression)) return true;
    await sleep(100);
  }
  return false;
}

const alertTextExpression = `document.querySelector('[role=alertdialog]')?.textContent?.replace(/\\s+/g, ' ').trim() || null`;
const liveTextExpression = `Array.from(document.querySelectorAll('[role=status],[role=alert]')).map(e => e.textContent.trim()).join(' | ') || null`;

async function clickSelector(selector) {
  const box = await evaluate(`(() => {
    const el = document.querySelector(${JSON.stringify(selector)});
    if (!el) return null;
    el.scrollIntoView({ block: 'center', inline: 'center' });
    const rect = el.getBoundingClientRect();
    return { x: rect.x + rect.width / 2, y: rect.y + rect.height / 2, visible: rect.width > 0 && rect.height > 0 };
  })()`);

  if (!box?.visible) return false;

  await cdp.send('Input.dispatchMouseEvent', {
    type: 'mouseMoved', x: box.x, y: box.y, button: 'none'
  }, sessionId);
  await cdp.send('Input.dispatchMouseEvent', {
    type: 'mousePressed', x: box.x, y: box.y, button: 'left', clickCount: 1
  }, sessionId);
  await cdp.send('Input.dispatchMouseEvent', {
    type: 'mouseReleased', x: box.x, y: box.y, button: 'left', clickCount: 1
  }, sessionId);
  await sleep(350);
  return true;
}

async function setInput(selector, value) {
  const ok = await evaluate(`(() => {
    const el = document.querySelector(${JSON.stringify(selector)});
    if (!el) return false;
    el.value = ${JSON.stringify(value)};
    el.dispatchEvent(new Event('input', { bubbles: true }));
    return true;
  })()`);
  if (ok) await sleep(200);
  return ok;
}

/// Preenche um input localizando-o pelo atributo placeholder.
/// Evita seletores CSS com placeholder: caminhos do Windows contêm "\",
/// que é caractere de escape dentro de um attribute selector.
async function setInputByPlaceholder(placeholder, value) {
  const ok = await evaluate(`(() => {
    const el = Array.from(document.querySelectorAll('input'))
      .find(i => (i.getAttribute('placeholder') || '') === ${JSON.stringify(placeholder)});
    if (!el) return false;
    const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
    setter.call(el, ${JSON.stringify(value)});
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
    el.focus();
    el.blur();
    return el.value;
  })()`);
  if (ok !== false) await sleep(250);
  return typeof ok === 'string' && ok.length > 0;
}

async function clickPoint(x, y) {
  await cdp.send('Input.dispatchMouseEvent', {
    type: 'mouseMoved', x, y, button: 'none'
  }, sessionId);
  await cdp.send('Input.dispatchMouseEvent', {
    type: 'mousePressed', x, y, button: 'left', clickCount: 1
  }, sessionId);
  await cdp.send('Input.dispatchMouseEvent', {
    type: 'mouseReleased', x, y, button: 'left', clickCount: 1
  }, sessionId);
  await sleep(350);
}

async function clickText(text) {
  const box = await evaluate(`(() => {
    const el = Array.from(document.querySelectorAll('button, a')).find(node => (node.textContent || '').trim() === ${JSON.stringify(text)});
    if (!el) return null;
    el.scrollIntoView({ block: 'center', inline: 'center' });
    const rect = el.getBoundingClientRect();
    return { x: rect.x + rect.width / 2, y: rect.y + rect.height / 2, visible: rect.width > 0 && rect.height > 0 };
  })()`);

  if (!box?.visible) return false;

  await cdp.send('Input.dispatchMouseEvent', {
    type: 'mouseMoved', x: box.x, y: box.y, button: 'none'
  }, sessionId);
  await cdp.send('Input.dispatchMouseEvent', {
    type: 'mousePressed', x: box.x, y: box.y, button: 'left', clickCount: 1
  }, sessionId);
  await cdp.send('Input.dispatchMouseEvent', {
    type: 'mouseReleased', x: box.x, y: box.y, button: 'left', clickCount: 1
  }, sessionId);
  await sleep(350);
  return true;
}

async function debugPageText() {
  try {
    const text = await evaluate('document.body.innerText.replace(/\\s+/g, " ").trim().slice(0, 240)');
    return text || '(vazio)';
  } catch {
    return '(indisponível)';
  }
}

async function ensureDictionaryFixture(interaction, problems) {
  console.error('[qa] ensureDictionary check');
  const hasDictionary = await evaluate(`!!document.querySelector('button[aria-label^="Excluir dicionário "]')`);
  console.error('[qa] ensureDictionary hasDictionary=' + hasDictionary);

  if (!hasDictionary) {
    const opened = await clickText('Novo dicionário');
    if (!opened) {
      problems.push(`${interaction.name}: não foi possível abrir o modal de novo dicionário. Página: ${await debugPageText()}`);
      return;
    }

    const waited = await waitForText('Criar dicionário');
    if (!waited) {
      problems.push(`${interaction.name}: modal de novo dicionário não apareceu.`);
      return;
    }

    const nameOk = await setInputByPlaceholder('Principal', 'Dicionário QA');
    if (!nameOk) {
      problems.push(`${interaction.name}: não foi possível preencher o nome do dicionário de teste.`);
      return;
    }

    const created = await clickText('Criar dicionário');
    if (!created) {
      problems.push(`${interaction.name}: não foi possível criar o dicionário de teste.`);
      return;
    }

    // CreateDictAsync já abre o dicionário criado (entidades ficam visíveis).
    const ok = await waitForText('Dicionário QA');
    if (!ok) {
      problems.push(`${interaction.name}: dicionário de teste não apareceu. Página: ${await debugPageText()}`);
      return;
    }
  }
  else {
    // Banco já continha dicionário: abre o primeiro para expor as entidades.
    const opened = await clickSelector('button[aria-label^="Abrir dicionário "]');
    if (!opened) {
      problems.push(`${interaction.name}: não foi possível abrir o dicionário existente.`);
      return;
    }
    if (!await waitForText('Entidades')) {
      problems.push(`${interaction.name}: painel de entidades não apareceu. Página: ${await debugPageText()}`);
      return;
    }
  }

  // O diagrama ER só renderiza a toolbar (inclusive "Refazer layout automático")
  // quando o dicionário tem entidades. Cria duas para exercitar também relações.
  for (const entityName of ['Customer', 'Order']) {
    const exists = await evaluate(`Array.from(document.querySelectorAll('button[aria-label^="Editar entidade "]'))
      .some(b => (b.getAttribute('aria-label') || '').includes(${JSON.stringify(entityName)}))`);
    console.error('[qa] ensureDictionary entity ' + entityName + ' exists=' + exists);

    if (exists) continue;

    const modalOpened = await clickText('Nova entidade');
    if (!modalOpened) {
      problems.push(`${interaction.name}: não foi possível abrir o modal de nova entidade. Página: ${await debugPageText()}`);
      return;
    }

    if (!await waitForText('Criar entidade')) {
      problems.push(`${interaction.name}: modal de nova entidade não apareceu.`);
      return;
    }

    // Placeholder "Customer" (nome) e "Customers" (tabela, opcional).
    if (!await setInputByPlaceholder('Customer', entityName)) {
      problems.push(`${interaction.name}: não foi possível preencher o nome da entidade '${entityName}'.`);
      return;
    }
    if (!await setInputByPlaceholder('Customers', entityName + 's')) {
      problems.push(`${interaction.name}: não foi possível preencher a tabela da entidade '${entityName}'.`);
      return;
    }

    const entityCreated = await clickText('Criar entidade');
    if (!entityCreated) {
      problems.push(`${interaction.name}: não foi possível criar a entidade '${entityName}'.`);
      return;
    }

    const entityVisible = await waitForText(entityName);
    if (!entityVisible) {
      problems.push(`${interaction.name}: entidade '${entityName}' não apareceu. Página: ${await debugPageText()}`);
      return;
    }
  }
}

async function runInteraction(interaction, problems) {
  if (interaction.ensureDictionary) {
    await ensureDictionaryFixture(interaction, problems);
    return;
  }

  if (interaction.ensureProject) {
    console.error('[qa] ensureProject check');
    const hasProject = await evaluate(`!!document.querySelector('button[aria-label^="Abrir "]')`);
    console.error('[qa] ensureProject hasProject=' + hasProject);
    if (!hasProject) {
      const opened = await clickText('Novo projeto');
      if (!opened) {
        problems.push(`${interaction.name}: não foi possível abrir o modal de novo projeto. Página: ${await debugPageText()}`);
        return;
      }

      const waited = await waitForText('Criar projeto');
      if (!waited) {
        problems.push(`${interaction.name}: modal de novo projeto não apareceu.`);
        return;
      }

      const nameOk = await setInputByPlaceholder('Meu Sistema', 'Projeto QA Gate');
      // O placeholder do diretório é "E:\Projetos\MeuSistema" (com barra invertida).
      const pathOk = await setInputByPlaceholder('E:\\Projetos\\MeuSistema', projectFixtureDir);
      if (!nameOk || !pathOk) {
        problems.push(`${interaction.name}: não foi possível preencher nome/caminho do projeto de teste.`);
        return;
      }

      const created = await clickText('Criar projeto');
      if (!created) {
        problems.push(`${interaction.name}: não foi possível criar o projeto de teste.`);
        return;
      }

      const registered = await waitForText('Projeto QA Gate');
      if (!registered) {
        problems.push(`${interaction.name}: projeto de teste não foi registrado. Página: ${await debugPageText()}`);
        return;
      }
    }

    const openedProject = await clickSelector('button[aria-label^="Abrir "]');
    if (!openedProject) {
      problems.push(`${interaction.name}: não foi possível abrir o projeto.`);
      return;
    }

    const ready = await waitForText('aberto.');
    if (!ready) {
      problems.push(`${interaction.name}: projeto não ficou ativo. Página: ${await debugPageText()}`);
      return;
    }
  }

  for (const step of interaction.steps) {
    if (step.click) {
      const clicked = await clickText(step.click);
      if (!clicked) {
        problems.push(`${interaction.name}: não achou o controle "${step.click}". Página: ${await debugPageText()}`);
        return;
      }
      await sleep(500);
    }

    if (step.clickFirst) {
      const clicked = await clickSelector(step.clickFirst);
      if (!clicked) {
        problems.push(`${interaction.name}: não achou o seletor "${step.clickFirst}". Página: ${await debugPageText()}`);
        return;
      }
      await sleep(500);
    }

    if (step.waitText) {
      const ok = await waitForText(step.waitText);
      if (!ok) {
        problems.push(`${interaction.name}: não apareceu "${step.waitText}". Página: ${await debugPageText()}`);
        return;
      }
    }

    if (step.expectFocusText) {
      const focusText = await evaluate('document.activeElement?.textContent?.trim() || ""');
      if (!focusText.includes(step.expectFocusText)) {
        problems.push(`${interaction.name}: foco inicial não está em "${step.expectFocusText}". Foco: ${focusText || '(nenhum)'}`);
        return;
      }
    }

    if (step.clickPoint) {
      await clickPoint(step.clickPoint.x, step.clickPoint.y);
    }

    if (step.expectLiveText) {
      const liveText = await evaluate(liveTextExpression);
      if (!liveText?.includes(step.expectLiveText)) {
        problems.push(`${interaction.name}: região viva não contém "${step.expectLiveText}". Valor: ${liveText}`);
        return;
      }
    }

    if (step.expectAlertText) {
      const alertText = await evaluate(alertTextExpression);
      if (!alertText?.includes(step.expectAlertText)) {
        problems.push(`${interaction.name}: alertdialog não contém "${step.expectAlertText}". Valor: ${alertText}`);
        return;
      }
    }

    if (step.expectNoAlert) {
      await sleep(300);
      const hasAlert = await evaluate(`!!document.querySelector('[role=alertdialog]')`);
      if (hasAlert) {
        problems.push(`${interaction.name}: alertdialog continuou aberto após cancelar.`);
        return;
      }
    }
  }
}

const problems = [];
const routeResults = [];

console.error('[qa] starting gate for ' + routes.length + ' routes');

for (const route of routes) {
  try {
    const routeStart = Date.now();
    const data = await gotoRoute(route);
    routeResults.push({ route, ...data, ms: Date.now() - routeStart, consoleErrors: [...consoleErrors], pageErrors: [...pageErrors] });
    console.error('[qa] route ' + route + ' ' + (Date.now() - routeStart) + 'ms');
    if (routeResults.length % 5 === 0) console.error('[qa] routes ' + routeResults.length + '/' + routes.length);

    if (data.h1 !== 1) problems.push(`${route}: esperado 1 H1, encontrado ${data.h1}.`);
    if (data.unlabeledControls > 0) problems.push(`${route}: ${data.unlabeledControls} controle(s) sem nome acessível.`);
    if (data.duplicateTableLabels > 0) problems.push(`${route}: ${data.duplicateTableLabels} nome(s) de ação repetido(s) dentro de tabelas.`);
    if (data.replacementChars) problems.push(`${route}: caractere de substituição detectado no conteúdo.`);
    if (data.tabsWithoutRole > 0) problems.push(`${route}: ${data.tabsWithoutRole} aba(s) do painel sem role="tab".`);
    if (consoleErrors.length) problems.push(`${route}: erro de console: ${consoleErrors[0]}`);
    if (pageErrors.length) problems.push(`${route}: pageerror: ${pageErrors[0]}`);
  } catch (error) {
    problems.push(`${route}: falha ao auditar — ${error.message}`);
  }
}

const interactionResults = [];

for (const interaction of interactions) {
  const before = problems.length;
  try {
    console.error('[qa] interaction ' + interaction.name);
    await gotoRoute(interaction.route);
    await runInteraction(interaction, problems);
    console.error('[qa] interaction done ' + interaction.name);
    if (consoleErrors.length) {
      problems.push(`${interaction.route}: erro de console durante "${interaction.name}": ${consoleErrors[0]}`);
    }
    if (pageErrors.length) {
      problems.push(`${interaction.route}: pageerror durante "${interaction.name}": ${pageErrors[0]}`);
    }
    interactionResults.push({
      route: interaction.route,
      name: interaction.name,
      ok: problems.length === before
    });
  } catch (error) {
    problems.push(`${interaction.route}: falha na interação "${interaction.name}" — ${error.message}`);
    interactionResults.push({ route: interaction.route, name: interaction.name, ok: false });
  }
}

await cdp.send('Target.closeTarget', { targetId }).catch(() => {});
ws.close();
browser.kill('SIGTERM');
await Promise.race([browserExit, sleep(3000)]);
if (!browserClosed) browser.kill('SIGKILL');
fs.rmSync(profileDir, { recursive: true, force: true });

const report = {
  ok: problems.length === 0,
  baseUrl,
  routesAudited: routeResults.length,
  interactionsAudited: interactionResults.length,
  routeResults,
  interactionResults,
  problems
};

console.log(JSON.stringify(report, null, 2));
process.exit(problems.length === 0 ? 0 : 1);
