/* ============================================================================
 * er-diagram.js — canvas interativo do Diagrama ER (XForge IDE).
 *
 * Modulo ES carregado sob demanda (dynamic import) pelo componente
 * ErDiagramCanvas. Responsavel por:
 *   - pan (arrastar o fundo) e zoom (Ctrl+scroll e botoes) via UM UNICO
 *     transform CSS no "world" — nada de re-render do Blazor por frame;
 *   - arrastar nos: durante o arrasto so mexemos transform do no e o "d"
 *     das arestas incidentes; a posicao final volta para o C# (persistencia);
 *   - geometria das arestas: bezier entre as BORDAS dos nos, notacao
 *     "pe de galinha" (crow's foot) e rotulo de cardinalidade na ponta;
 *   - tooltip de hover com os campos da entidade.
 *
 * O modelo (nos + arestas + campos) chega como JSON vindo do C#, que e a
 * fonte da verdade de posicoes e selecao.
 * ==========================================================================*/

const NS = 'http://www.w3.org/2000/svg';

const MIN_SCALE = 0.2;
const MAX_SCALE = 2.5;
const FIT_MAX_SCALE = 1.4;
const GRID = 8;          // engrade ao arrastar (0 desativa o snap)
const CTRL_MAX = 110;    // deslocamento maximo dos pontos de controle do bezier
const TIP_DELAY = 180;   // ms antes de mostrar o tooltip

/** Sessoes ativas indexadas pelo id do viewport (permite mais de um canvas). */
const sessions = new Map();

function clamp(v, lo, hi) { return v < lo ? lo : (v > hi ? hi : v); }

/** Cria elemento SVG já com atributos. */
function svgEl(tag, attrs) {
  const el = document.createElementNS(NS, tag);
  if (attrs) {
    for (const k in attrs) {
      if (Object.prototype.hasOwnProperty.call(attrs, k)) el.setAttribute(k, attrs[k]);
    }
  }
  return el;
}

/** Notifica o componente Blazor sem nunca estourar (circuito pode ter caido). */
function notify(s, name) {
  const ref = s && s.dotNetRef;
  if (!ref) return;
  const args = Array.prototype.slice.call(arguments, 2);
  try {
    const p = ref.invokeMethodAsync.apply(ref, [name].concat(args));
    if (p && typeof p.catch === 'function') p.catch(function () { /* ignorado */ });
  } catch (e) { /* circuito encerrado */ }
}

/* ============================================================================
 * GEOMETRIA DAS ARESTAS
 * ==========================================================================*/

/** Ponto da borda do retangulo na direcao de (tx,ty), saido do centro. */
function anchor(rect, tx, ty) {
  const cx = rect.x + rect.w / 2;
  const cy = rect.y + rect.h / 2;
  const dx = tx - cx;
  const dy = ty - cy;
  if (!dx && !dy) return { x: cx, y: cy };
  const sx = dx === 0 ? Infinity : (rect.w / 2) / Math.abs(dx);
  const sy = dy === 0 ? Infinity : (rect.h / 2) / Math.abs(dy);
  const k = Math.min(sx, sy);
  return { x: cx + dx * k, y: cy + dy * k };
}

/** bezier cubica ligando a borda de A a borda de B (horizontal ou vertical). */
function edgeGeometry(a, b) {
  const ca = { x: a.x + a.w / 2, y: a.y + a.h / 2 };
  const cb = { x: b.x + b.w / 2, y: b.y + b.h / 2 };
  const p1 = anchor(a, cb.x, cb.y);
  const p2 = anchor(b, ca.x, ca.y);
  const dx = p2.x - p1.x;
  const dy = p2.y - p1.y;
  const k = clamp(Math.max(Math.abs(dx), Math.abs(dy)) * 0.45, 40, CTRL_MAX);
  let c1;
  let c2;
  if (Math.abs(dx) >= Math.abs(dy)) {
    const sgn = dx === 0 ? 1 : (dx > 0 ? 1 : -1);
    c1 = { x: p1.x + sgn * k, y: p1.y };
    c2 = { x: p2.x - sgn * k, y: p2.y };
  } else {
    const sgn = dy === 0 ? 1 : (dy > 0 ? 1 : -1);
    c1 = { x: p1.x, y: p1.y + sgn * k };
    c2 = { x: p2.x, y: p2.y - sgn * k };
  }
  return { p1: p1, p2: p2, c1: c1, c2: c2 };
}

/**
 * Marcador de cardinalidade em p, entrando no no pela direcao "inward".
 * many=false -> barra "1"; many=true -> pe de galinha "N".
 */
function markerPath(p, inward, many) {
  const px = -inward.y;   // perpendicular a direcao da ligacao
  const py = inward.x;
  const bx = p.x + inward.x * 9;
  const by = p.y + inward.y * 9;
  const h = 7;
  if (!many) {
    return 'M ' + (bx + px * h) + ' ' + (by + py * h) + ' L ' + (bx - px * h) + ' ' + (by - py * h);
  }
  return [
    'M ' + p.x + ' ' + p.y + ' L ' + (bx + px * h) + ' ' + (by + py * h),
    'M ' + p.x + ' ' + p.y + ' L ' + bx + ' ' + by,
    'M ' + p.x + ' ' + p.y + ' L ' + (bx - px * h) + ' ' + (by - py * h)
  ].join(' ');
}

/* ============================================================================
 * CONSTRUCAO / ATUALIZACAO DAS ARESTAS
 * ==========================================================================*/

function buildEdge(rec) {
  const g = svgEl('g', { 'class': 'ide-er__edge ide-er__edge--' + rec.tone, 'data-er-edge': rec.id });
  const hit = svgEl('path', { 'class': 'ide-er__edge__hit', 'fill': 'none' });
  const line = svgEl('path', { 'class': 'ide-er__edge__line', 'fill': 'none' });
  const feet = svgEl('path', { 'class': 'ide-er__edge__foot', 'fill': 'none' });
  const t1 = svgEl('text', { 'class': 'ide-er__edge__label', 'text-anchor': 'middle' });
  t1.textContent = rec.fromCard;
  const t2 = svgEl('text', { 'class': 'ide-er__edge__label', 'text-anchor': 'middle' });
  t2.textContent = rec.toCard;
  g.appendChild(hit);
  g.appendChild(line);
  g.appendChild(feet);
  g.appendChild(t1);
  g.appendChild(t2);
  rec.g = g;
  rec.hit = hit;
  rec.line = line;
  rec.feet = feet;
  rec.t1 = t1;
  rec.t2 = t2;
  return g;
}

/** Recalcula d/marcadores/rotulos de todas as arestas (sem recriar o DOM). */
function updateEdges(s) {
  for (let i = 0; i < s.edges.length; i++) {
    const e = s.edges[i];
    const a = s.rects.get(e.from);
    const b = s.rects.get(e.to);
    if (!a || !b) { e.g.style.display = 'none'; continue; }
    e.g.style.display = '';
    const g = edgeGeometry(a, b);
    const d = 'M ' + g.p1.x + ' ' + g.p1.y + ' C ' + g.c1.x + ' ' + g.c1.y + ', ' +
      g.c2.x + ' ' + g.c2.y + ', ' + g.p2.x + ' ' + g.p2.y;
    e.line.setAttribute('d', d);
    e.hit.setAttribute('d', d);
    const dx = g.p2.x - g.p1.x;
    const dy = g.p2.y - g.p1.y;
    const len = Math.sqrt(dx * dx + dy * dy) || 1;
    const ux = dx / len;
    const uy = dy / len;
    const perp = { x: -uy, y: ux };
    const inA = { x: -ux, y: -uy };
    const inB = { x: ux, y: uy };
    e.feet.setAttribute('d', markerPath(g.p1, inA, e.fromMany) + ' ' + markerPath(g.p2, inB, e.toMany));
    // Rótulos ficam fora das caixas, ao longo da ligação (mesmo lado do perpendicular).
    placeLabel(e.t1, g.p1, ux, uy, perp);
    placeLabel(e.t2, g.p2, -ux, -uy, perp);
  }
}

/** Rótulo de cardinalidade na ponta, deslocado 16px ao longo da ligação. */
function placeLabel(t, p, dx, dy, perp) {
  t.setAttribute('x', p.x + dx * 16 + perp.x * 11);
  t.setAttribute('y', p.y + dy * 16 + perp.y * 11);
}

/* ============================================================================
 * TOOLTIP DE HOVER
 * ==========================================================================*/

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text !== undefined && text !== null) n.textContent = String(text);
  return n;
}

function buildTip(n) {
  const root = el('div', 'ide-er__tip-box');
  const head = el('div', 'ide-er__tip-head');
  head.appendChild(el('span', 'ide-er__tip-name', n.name));
  head.appendChild(el('span', 'ide-er__tip-table', n.table));
  root.appendChild(head);

  root.appendChild(el('div', 'ide-er__tip-meta',
    n.count + (n.count === 1 ? ' campo' : ' campos') + ' · ' + n.table));

  const list = el('div', 'ide-er__tip-list');
  for (let i = 0; i < n.fields.length; i++) {
    const f = n.fields[i];
    const row = el('div', 'ide-er__tip-row');
    if (f.pk) row.classList.add('is-pk');
    if (f.fk) row.classList.add('is-fk');
    row.appendChild(el('span', 'ide-er__tip-key', f.pk ? 'PK' : (f.fk ? 'FK' : '')));
    row.appendChild(el('span', 'ide-er__tip-field', f.name));
    row.appendChild(el('span', 'ide-er__tip-type', f.type));
    if (f.req) row.appendChild(el('span', 'ide-er__tip-req', '*'));
    list.appendChild(row);
  }
  if (n.fields.length === 0) {
    list.appendChild(el('div', 'ide-er__tip-row is-empty', 'sem campos'));
  }
  root.appendChild(list);
  return root;
}

function showTip(s, nodeId) {
  if (!s.tip || !s.model) return;
  const found = s.model.nodes.filter(function (x) { return x.id === nodeId; })[0];
  if (!found) return;
  const r = s.rects.get(nodeId);
  if (!r) return;

  s.tip.replaceChildren(buildTip(found));
  // Precisa estar visível ANTES de medir (elemento oculto tem offsetWidth 0).
  s.tip.hidden = false;

  const vw = s.viewport.clientWidth;
  const vh = s.viewport.clientHeight;
  const tw = s.tip.offsetWidth;
  const th = s.tip.offsetHeight;
  const right = r.x * s.scale + s.tx + r.w * s.scale + 10;
  const left0 = r.x * s.scale + s.tx - tw - 10;
  let left = right;
  if (left + tw > vw - 6) left = left0;
  if (left < 6) left = Math.max(6, Math.min(right, vw - tw - 6));
  let top = r.y * s.scale + s.ty;
  if (top + th > vh - 6) top = Math.max(6, vh - th - 6);
  s.tip.style.left = Math.round(left) + 'px';
  s.tip.style.top = Math.round(top) + 'px';
}

function hideTip(s) {
  if (s && s.tip) s.tip.hidden = true;
  if (s) { s.hover = null; }
}

/* ============================================================================
 * VIEWPORT: PAN / ZOOM / DRAG
 * ==========================================================================*/

function applyTransform(s) {
  s.world.style.transform = 'translate(' + s.tx + 'px, ' + s.ty + 'px) scale(' + s.scale + ')';
}

function reportZoom(s) {
  notify(s, 'OnZoomChanged', Math.round(s.scale * 100));
}

function zoomAt(s, cx, cy, factor) {
  const next = clamp(s.scale * factor, MIN_SCALE, MAX_SCALE);
  const k = next / s.scale;
  s.tx = cx - (cx - s.tx) * k;
  s.ty = cy - (cy - s.ty) * k;
  s.scale = next;
  s.autoFit = false;   // zoom manual: o resize para de reajustar
  applyTransform(s);
  reportZoom(s);
}

function boundsOf(s) {
  let x0 = Infinity;
  let y0 = Infinity;
  let x1 = -Infinity;
  let y1 = -Infinity;
  s.rects.forEach(function (r) {
    x0 = Math.min(x0, r.x);
    y0 = Math.min(y0, r.y);
    x1 = Math.max(x1, r.x + r.w);
    y1 = Math.max(y1, r.y + r.h);
  });
  if (x0 === Infinity) return { x: 0, y: 0, w: 1, h: 1 };
  return { x: x0, y: y0, w: Math.max(1, x1 - x0), h: Math.max(1, y1 - y0) };
}

function fitView(s, padding) {
  const vw = s.viewport.clientWidth;
  const vh = s.viewport.clientHeight;
  const b = boundsOf(s);
  const p = typeof padding === 'number' ? padding : 24;
  const sc = clamp(Math.min((vw - p * 2) / b.w, (vh - p * 2) / b.h), MIN_SCALE, FIT_MAX_SCALE);
  s.scale = sc;
  s.tx = (vw - b.w * sc) / 2 - b.x * sc;
  s.ty = (vh - b.h * sc) / 2 - b.y * sc;
  s.autoFit = true;    // encaixe explícito: volta a acompanhar o resize
  applyTransform(s);
  reportZoom(s);
}

/* ---------------------------------- eventos -------------------------------- */

function onDown(e) {
  const s = sessions.get(e.currentTarget.getAttribute('data-er-session'));
  if (!s || (e.button !== undefined && e.button !== 0)) return;
  const nodeEl = e.target.closest ? e.target.closest('[data-er-node]') : null;
  const edgeEl = e.target.closest ? e.target.closest('[data-er-edge]') : null;
  hideTip(s);
  if (nodeEl) {
    const id = nodeEl.getAttribute('data-er-node');
    const r = s.rects.get(id);
    if (!r) return;
    try { s.viewport.setPointerCapture(e.pointerId); } catch (err) { /* ignorado */ }
    s.drag = { id: id, el: nodeEl, sx: e.clientX, sy: e.clientY, ox: r.x, oy: r.y, moved: false };
    s.viewport.classList.add('is-dragging');
    notify(s, 'OnNodeDragStarted');
    return;
  }
  try { s.viewport.setPointerCapture(e.pointerId); } catch (err) { /* ignorado */ }
  // Clicar numa aresta seleciona; arrastar a partir dela continua sendo pan.
  const edgeId = edgeEl ? edgeEl.getAttribute('data-er-edge') : null;
  s.pan = {
    sx: e.clientX, sy: e.clientY, ox: s.tx, oy: s.ty, moved: false,
    edgeId: edgeId
  };
  if (edgeId) notify(s, 'OnEdgeSelected', edgeId);
  s.viewport.classList.add('is-panning');
}

function onMove(e) {
  const s = sessions.get(e.currentTarget.getAttribute('data-er-session'));
  if (!s) return;
  if (s.drag) {
    const d = s.drag;
    const dx = (e.clientX - d.sx) / s.scale;
    const dy = (e.clientY - d.sy) / s.scale;
    if (Math.abs(dx) > 3 || Math.abs(dy) > 3) d.moved = true;
    let nx = d.ox + dx;
    let ny = d.oy + dy;
    if (GRID > 0) {
      nx = Math.round(nx / GRID) * GRID;
      ny = Math.round(ny / GRID) * GRID;
    }
    const r = s.rects.get(d.id);
    if (r) { r.x = nx; r.y = ny; }
    d.el.style.transform = 'translate(' + nx + 'px, ' + ny + 'px)';
    d.el.setAttribute('data-er-x', String(nx));
    d.el.setAttribute('data-er-y', String(ny));
    updateEdges(s);
    return;
  }
  if (s.pan) {
    s.tx = s.pan.ox + (e.clientX - s.pan.sx);
    s.ty = s.pan.oy + (e.clientY - s.pan.sy);
    if (Math.abs(e.clientX - s.pan.sx) > 3 || Math.abs(e.clientY - s.pan.sy) > 3) s.pan.moved = true;
    applyTransform(s);
  }
}

function onUp(e) {
  const s = sessions.get(e.currentTarget.getAttribute('data-er-session'));
  if (!s) return;
  if (s.drag) {
    const d = s.drag;
    s.viewport.classList.remove('is-dragging');
    const x = Number(d.el.getAttribute('data-er-x'));
    const y = Number(d.el.getAttribute('data-er-y'));
    if (d.moved) notify(s, 'OnNodeMoved', d.id, x, y);
    else notify(s, 'OnNodeSelected', d.id);
    s.drag = null;
    notify(s, 'OnNodeDragEnded');
  }
  if (s.pan) {
    s.viewport.classList.remove('is-panning');
    if (s.pan.moved) { s.autoFit = false; notify(s, 'OnViewMoved'); }
    else if (!s.pan.edgeId) notify(s, 'OnBackgroundSelected');
    s.pan = null;
  }
  try {
    if (s.viewport.hasPointerCapture && s.viewport.hasPointerCapture(e.pointerId)) {
      s.viewport.releasePointerCapture(e.pointerId);
    }
  } catch (err) { /* ignorado */ }
}

function onWheel(e) {
  const s = sessions.get(e.currentTarget.getAttribute('data-er-session'));
  if (!s) return;
  e.preventDefault();
  if (e.ctrlKey || e.metaKey) {
    const box = s.viewport.getBoundingClientRect();
    zoomAt(s, e.clientX - box.left, e.clientY - box.top, Math.exp(-e.deltaY * 0.0018));
    return;
  }
  s.tx -= e.deltaX;
  s.ty -= e.deltaY;
  if (e.deltaX || e.deltaY) s.autoFit = false;
  applyTransform(s);
  hideTip(s);
}

function onDblClick(e) {
  const s = sessions.get(e.currentTarget.getAttribute('data-er-session'));
  if (!s) return;
  if (e.target.closest && e.target.closest('[data-er-node]')) return;
  fitView(s, 24);
  notify(s, 'OnViewMoved');
}

function onOver(e) {
  const s = sessions.get(e.currentTarget.getAttribute('data-er-session'));
  if (!s || !s.tip) return;
  const nodeEl = e.target.closest ? e.target.closest('[data-er-node]') : null;
  if (!nodeEl) return;
  const id = nodeEl.getAttribute('data-er-node');
  if (s.hover === id) return;
  s.hover = id;
  window.clearTimeout(s.tipTimer);
  s.tipTimer = window.setTimeout(function () { showTip(s, id); }, TIP_DELAY);
}

function onOut(e) {
  const s = sessions.get(e.currentTarget.getAttribute('data-er-session'));
  if (!s) return;
  const nodeEl = e.target.closest ? e.target.closest('[data-er-node]') : null;
  if (!nodeEl) return;
  const to = e.relatedTarget;
  if (to && nodeEl.contains(to)) return;
  s.hover = null;
  window.clearTimeout(s.tipTimer);
  hideTip(s);
}

function onKey(e) {
  const s = sessions.get(e.currentTarget.getAttribute('data-er-session'));
  if (!s) return;
  const step = e.shiftKey ? 120 : 40;
  switch (e.key) {
    case 'Escape': notify(s, 'OnBackgroundSelected'); break;
    case 'ArrowLeft': s.tx += step; applyTransform(s); e.preventDefault(); break;
    case 'ArrowRight': s.tx -= step; applyTransform(s); e.preventDefault(); break;
    case 'ArrowUp': s.ty += step; applyTransform(s); e.preventDefault(); break;
    case 'ArrowDown': s.ty -= step; applyTransform(s); e.preventDefault(); break;
    case '+': case '=': zoomAt(s, s.viewport.clientWidth / 2, s.viewport.clientHeight / 2, 1.2); e.preventDefault(); break;
    case '-': case '_': zoomAt(s, s.viewport.clientWidth / 2, s.viewport.clientHeight / 2, 1 / 1.2); e.preventDefault(); break;
    default: break;
  }
}

/* ============================================================================
 * API PUBLICA (chamada pelo componente Blazor)
 * ==========================================================================*/

/** Liga os eventos no viewport e devolve o tamanho visivel. */
export function attach(viewportId, dotNetRef) {
  dispose(viewportId);
  const viewport = document.getElementById(viewportId);
  if (!viewport) return null;
  const world = viewport.querySelector('[data-er-world]');
  const wires = viewport.querySelector('[data-er-wires]');
  if (!world || !wires) return null;
  const tip = viewport.querySelector('[data-er-tip]');

  const s = {
    viewport: viewport,
    world: world,
    wires: wires,
    tip: tip,
    dotNetRef: dotNetRef,
    scale: 1,
    tx: 0,
    ty: 0,
    rects: new Map(),
    edges: [],
    model: null,
    byId: new Map(),
    drag: null,
    pan: null,
    hover: null,
    tipTimer: 0,
    autoFit: true,
    lastW: viewport.clientWidth,
    lastH: viewport.clientHeight
  };
  sessions.set(viewportId, s);
  viewport.setAttribute('data-er-session', viewportId);

  viewport.addEventListener('pointerdown', onDown);
  viewport.addEventListener('pointermove', onMove);
  viewport.addEventListener('pointerup', onUp);
  viewport.addEventListener('pointercancel', onUp);
  viewport.addEventListener('wheel', onWheel, { passive: false });
  viewport.addEventListener('dblclick', onDblClick);
  viewport.addEventListener('pointerover', onOver);
  viewport.addEventListener('pointerout', onOut);
  viewport.addEventListener('keydown', onKey);

  // A primeira chamada do ResizeObserver chega junto com o observe(): só registra
  // o tamanho. Reajusta sozinho enquanto o usuário não tiver movido o diagrama.
  s.observer = new ResizeObserver(function () {
    const w = viewport.clientWidth;
    const h = viewport.clientHeight;
    if (w === s.lastW && h === s.lastH) return;
    s.lastW = w;
    s.lastH = h;
    if (!s.autoFit) {
      notify(s, 'OnViewportResized', w, h);
      return;
    }
    fitView(s, 24);
  });
  s.observer.observe(viewport);

  return { width: viewport.clientWidth, height: viewport.clientHeight };
}

/** Aplica o modelo: posiciona nos, mede alturas reais e (re)desenha as arestas. */
export function applyModel(viewportId, json) {
  const s = sessions.get(viewportId);
  if (!s) return;
  let model;
  try { model = JSON.parse(json); } catch (err) { return; }
  s.model = model;

  s.byId = new Map();
  const list = s.viewport.querySelectorAll('[data-er-node]');
  for (let i = 0; i < list.length; i++) {
    s.byId.set(list[i].getAttribute('data-er-node'), list[i]);
  }

  const rects = new Map();
  for (let i = 0; i < model.nodes.length; i++) {
    const n = model.nodes[i];
    const nodeEl = s.byId.get(n.id);
    if (!nodeEl) continue;
    const x = Number(n.x) || 0;
    const y = Number(n.y) || 0;
    if (!s.drag || s.drag.id !== n.id) {
      nodeEl.style.transform = 'translate(' + x + 'px, ' + y + 'px)';
      nodeEl.setAttribute('data-er-x', String(x));
      nodeEl.setAttribute('data-er-y', String(y));
    }
    rects.set(n.id, {
      x: x,
      y: y,
      w: nodeEl.offsetWidth || Number(n.w) || 220,
      h: nodeEl.offsetHeight || 0
    });
  }
  s.rects = rects;

  const frag = document.createDocumentFragment();
  s.edges = [];
  for (let i = 0; i < model.edges.length; i++) {
    const e = model.edges[i];
    const rec = {
      id: e.id,
      from: e.from,
      to: e.to,
      tone: e.tone,
      fromMany: e.fromCard === 'N',
      toMany: e.toCard === 'N',
      fromCard: e.fromCard,
      toCard: e.toCard
    };
    frag.appendChild(buildEdge(rec));
    s.edges.push(rec);
  }
  s.wires.textContent = '';
  s.wires.appendChild(frag);
  updateEdges(s);
}

/** Zoom relativo em torno do centro do viewport. */
export function zoomBy(viewportId, factor) {
  const s = sessions.get(viewportId);
  if (!s) return;
  zoomAt(s, s.viewport.clientWidth / 2, s.viewport.clientHeight / 2, Number(factor) || 1);
}

/** Ajusta o diagrama ao tamanho visivel do viewport. */
export function fit(viewportId, padding) {
  const s = sessions.get(viewportId);
  if (!s) return;
  fitView(s, padding);
}

/** Volta para 100%, sem translacao. */
export function resetView(viewportId) {
  const s = sessions.get(viewportId);
  if (!s) return;
  s.scale = 1;
  s.tx = 0;
  s.ty = 0;
  s.autoFit = false;
  applyTransform(s);
  reportZoom(s);
}

/** Realce de selecao: no/aresta selecionados + relacionados; o resto escurece. */
export function setSelection(viewportId, json) {
  const s = sessions.get(viewportId);
  if (!s) return;
  let sel;
  try { sel = JSON.parse(json); } catch (err) { return; }
  const node = sel.node || null;
  const edge = sel.edge || null;
  const rn = new Set(sel.relatedNodes || []);
  const re = new Set(sel.relatedEdges || []);
  s.byId.forEach(function (nodeEl, id) {
    nodeEl.classList.toggle('is-selected', !!node && id === node);
    nodeEl.classList.toggle('is-related', !!node && rn.has(id));
    nodeEl.classList.toggle('is-dim', !!node && !(id === node || rn.has(id)));
  });
  for (let i = 0; i < s.edges.length; i++) {
    const e = s.edges[i];
    const on = !!edge && e.id === edge;
    const rel = !!node && re.has(e.id);
    e.g.classList.toggle('is-selected', on);
    e.g.classList.toggle('is-related', rel);
    e.g.classList.toggle('is-dim', !!node && !(on || rel));
  }
}

/** Desliga eventos e observadores de uma sessao. */
export function dispose(viewportId) {
  const s = sessions.get(viewportId);
  if (!s) return;
  const v = s.viewport;
  v.removeEventListener('pointerdown', onDown);
  v.removeEventListener('pointermove', onMove);
  v.removeEventListener('pointerup', onUp);
  v.removeEventListener('pointercancel', onUp);
  v.removeEventListener('wheel', onWheel);
  v.removeEventListener('dblclick', onDblClick);
  v.removeEventListener('pointerover', onOver);
  v.removeEventListener('pointerout', onOut);
  v.removeEventListener('keydown', onKey);
  if (s.observer) { s.observer.disconnect(); s.observer = null; }
  window.clearTimeout(s.tipTimer);
  sessions.delete(viewportId);
}
