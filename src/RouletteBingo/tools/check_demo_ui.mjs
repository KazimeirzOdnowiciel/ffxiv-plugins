// Boots the generated preview in a tiny fake DOM so the UI script is actually executed,
// not just generated. Catches missing element ids, typos and startup exceptions.
//
//   node tools/check_demo_ui.mjs

import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '..');
const htmlPath = path.join(root, 'preview', 'bingo-demo.html');

const html = fs.readFileSync(htmlPath, 'utf8');

const scripts = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map((m) => m[1]);
if (scripts.length !== 3) {
  console.error(`expected 3 inline scripts, found ${scripts.length}`);
  process.exit(1);
}

class El {
  constructor(tag = 'div') {
    this.tag = tag;
    this.className = '';
    this.children = [];
    this.dataset = {};
    this.style = { setProperty() {} };
    this.classList = { add() {}, remove() {}, contains: () => false };
    this._text = '';
    this._html = '';
    this.listeners = [];
  }

  append(...nodes) {
    this.children.push(...nodes);
  }

  appendChild(node) {
    this.children.push(node);
  }

  addEventListener(event, handler) {
    this.listeners.push([event, handler]);
  }

  set innerHTML(value) {
    this._html = value;
    if (value === '') this.children = [];
  }

  get innerHTML() {
    return this._html;
  }

  set textContent(value) {
    this._text = String(value);
  }

  get textContent() {
    return this._text;
  }
}

const ids = [
  'grid', 'duty-name', 'duty-meta', 'seed', 'progress', 'bar', 'lines',
  'code', 'toast', 'new-card', 'kind', 'cursed', 'copy-report', 'copy-code',
  'load', 'load-input', 'pool-size',
];

const registry = new Map(ids.map((id) => [id, new El()]));

const document = {
  getElementById: (id) => registry.get(id) ?? null,
  createElement: (tag) => new El(tag),
};

const sandbox = {
  document,
  crypto: {
    getRandomValues: (array) => {
      for (let i = 0; i < array.length; i++) array[i] = (Math.random() * 0xffffffff) >>> 0;
      return array;
    },
  },
  navigator: { clipboard: { writeText: async () => {} } },
  setTimeout,
  clearTimeout,
  console,
};

sandbox.globalThis = sandbox;
vm.createContext(sandbox);

for (const script of scripts) {
  try {
    vm.runInContext(script, sandbox, { filename: 'demo-inline.js' });
  } catch (error) {
    console.error('script threw at startup:', error);
    process.exit(1);
  }
}

const grid = registry.get('grid');
if (grid.children.length !== 25) {
  console.error(`grid rendered ${grid.children.length} cells, expected 25`);
  process.exit(1);
}

const first = grid.children[0];
if (!first.children || first.children.length < 2) {
  console.error('cell is missing its label or text');
  process.exit(1);
}

const texts = grid.children.map((cell) => cell.children[1]?.textContent ?? '');
if (texts.some((text) => !text || text.length < 4)) {
  console.error('a cell rendered without text');
  process.exit(1);
}

if (new Set(texts).size !== 25) {
  console.error('duplicate cell text on the board');
  process.exit(1);
}

if (!/^RB1\./.test(registry.get('code').textContent)) {
  console.error(`card code looks wrong: ${registry.get('code').textContent}`);
  process.exit(1);
}

if (!registry.get('duty-name').textContent) {
  console.error('duty name was not set');
  process.exit(1);
}

// Click a cell: the mask, the counter and the code all have to move.
const before = registry.get('progress').textContent;
first.listeners.find(([event]) => event === 'click')[1]();
const after = registry.get('progress').textContent;
if (before === after) {
  console.error('clicking a cell did not change the progress counter');
  process.exit(1);
}

console.log(`ui ok: 25 cells, code ${registry.get('code').textContent}, progress ${before} -> ${after}`);
