/* global POOL, BingoCore */

const core = globalThis.BingoCore;

const COLORS = {
  Free: '#dcb35c',
  Tank: '#6194d9',
  Healer: '#5cc28c',
  Damage: '#db6b6b',
  Social: '#a885e0',
  Loot: '#e6ae4d',
  Sprout: '#9ed161',
  Cursed: '#e6518c',
  Pvp: '#e08543',
  Alliance: '#4db8b8',
};

const CAT_NAMES = {
  Free: 'Free',
  Tank: 'Tank',
  Healer: 'Healer',
  Damage: 'Damage',
  Social: 'Party',
  Loot: 'Loot',
  Sprout: 'Sprout',
  Cursed: 'Cursed',
  Pvp: 'PvP',
  Alliance: 'Alliance',
};

const RARITY_PIPS = { Common: 1, Uncommon: 2, Rare: 3, Cursed: 4 };
const RARITY_NAMES = { Common: 'Common', Uncommon: 'Uncommon', Rare: 'Rare', Cursed: 'Cursed' };

const DUTY_NAMES = {
  Dungeon: ['The Tam-Tara Deepcroft', 'Sastasha', 'The Dusk Vigil', 'The Vault', 'Alzadaal\'s Legacy'],
  EightMan: ['The Weapon\'s Refrain', 'The Minstrel\'s Ballad: Shinryu', 'A Trial, Probably', 'The Navel (Hard)'],
  Alliance: ['The Syrcus Tower', 'The Weeping City of Mhach', 'The Orbonne Monastery', 'The Tower at Paradigm\'s Breach'],
  Pvp: ['Frontline: Onsal Hakair', 'Frontline: Seal Rock', 'Frontline: The Fields of Glory', 'Crystalline Conflict'],
};

const state = {
  seed: 0n,
  kind: 'Dungeon',
  cursed: false,
  mask: 1 << 12,
  cells: [],
  dutyName: '',
  autoFlash: 0,
};

function randomSeed() {
  const values = new Uint32Array(2);
  crypto.getRandomValues(values);
  return (BigInt(values[0]) << 32n) | BigInt(values[1]);
}

function pickDutyName(kind) {
  const names = DUTY_NAMES[kind] || ['A duty'];
  const index = Number(state.seed % BigInt(names.length));
  return names[index];
}

function roll(seed, kind, cursed) {
  state.seed = seed;
  state.kind = kind;
  state.cursed = cursed;
  state.cells = core.generate(seed, kind, POOL, cursed);
  state.dutyName = pickDutyName(kind);
  state.mask = 1 << 12;
  render();
  renderCode();
}

function rgba(hex, alpha) {
  const value = parseInt(hex.slice(1), 16);
  const r = (value >> 16) & 255;
  const g = (value >> 8) & 255;
  const b = value & 255;
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function render() {
  const grid = document.getElementById('grid');
  const lines = core.completedLines(state.mask);
  const inLine = new Set(lines.flatMap((line) => line.cells));

  document.getElementById('duty-name').textContent = state.dutyName;
  document.getElementById('duty-meta').textContent =
    `${core.kindLabel(state.kind)}${state.cursed ? '  -  cursed mode' : ''}`;
  document.getElementById('seed').textContent = `seed ${state.seed.toString(16)}`;

  grid.innerHTML = '';
  state.cells.forEach((cell, index) => {
    const marked = (state.mask & (1 << index)) !== 0;
    const color = COLORS[cell.cat] || '#8b93a5';

    const node = el('div', 'cell');
    node.dataset.index = String(index);
    node.style.setProperty('--cat', color);

    if (marked) {
      node.classList.add('marked');
      node.style.background = rgba(color, 0.16);
      node.style.borderColor = rgba(color, 0.55);
    }

    if (inLine.has(index)) node.classList.add('bingo');

    const head = el('div', 'cell-head');
    const dot = el('span', 'dot');
    dot.style.background = color;
    const cat = el('span', 'cat', CAT_NAMES[cell.cat] || cell.cat);
    cat.style.color = color;
    head.append(dot, cat);
    node.append(head);

    node.append(el('div', 'txt', cell.text));

    const foot = el('div', 'cell-foot');
    const pips = el('span', 'pips');
    for (let i = 0; i < (RARITY_PIPS[cell.rarity] || 1); i++) {
      const pip = el('i');
      pip.style.background = marked ? 'rgba(255,255,255,.25)' : color;
      pips.append(pip);
    }
    foot.append(pips);
    if (marked) foot.append(el('span', 'check', '\u2713'));
    node.append(foot);

    node.title = `${CAT_NAMES[cell.cat]} - ${RARITY_NAMES[cell.rarity]}\n${cell.text}`;

    node.addEventListener('click', () => {
      const bit = 1 << index;
      const wasMarked = (state.mask & bit) !== 0;
      const before = core.completedLines(state.mask).length;
      state.mask ^= bit;

      const after = core.completedLines(state.mask);
      if (after.length > before) toast(`BINGO - ${after[after.length - 1].label}`);
      if (!wasMarked && index === 12) toast('The free square is always free.');

      render();
      renderCode();
    });

    grid.append(node);
  });

  const count = state.cells.filter((_, i) => state.mask & (1 << i)).length;
  document.getElementById('progress').textContent = `${count}/25`;
  document.getElementById('bar').style.width = `${(count / 25) * 100}%`;

  const lineList = document.getElementById('lines');
  lineList.innerHTML = '';
  if (lines.length === 0) {
    lineList.append(el('span', 'muted', 'no bingo yet - five in a row does it'));
  } else {
    lines.forEach((line) => {
      const chip = el('span', 'chip', `BINGO: ${line.label}`);
      lineList.append(chip);
    });
  }
}

function renderCode() {
  const code = core.encodeCode(state.seed, state.mask, state.kind);
  document.getElementById('code').textContent = code;
  return code;
}

let toastTimer = 0;

function toast(message) {
  const node = document.getElementById('toast');
  node.textContent = message;
  node.classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => node.classList.remove('show'), 2200);
}

async function copy(text, label) {
  try {
    await navigator.clipboard.writeText(text);
    toast(`${label} copied`);
  } catch {
    toast('Clipboard blocked by the browser');
  }
}

function wire() {
  document.getElementById('new-card').addEventListener('click', () => roll(randomSeed(), state.kind, state.cursed));

  document.getElementById('kind').addEventListener('change', (event) => {
    roll(randomSeed(), event.target.value, state.cursed);
  });

  document.getElementById('cursed').addEventListener('change', (event) => {
    roll(randomSeed(), state.kind, event.target.checked);
  });

  document.getElementById('copy-report').addEventListener('click', () => {
    const text = core.report(state.cells, state.mask, state.seed, state.kind, state.dutyName);
    copy(text, 'Report');
  });

  document.getElementById('copy-code').addEventListener('click', () => copy(renderCode(), 'Card code'));

  document.getElementById('load').addEventListener('click', () => {
    const input = document.getElementById('load-input');
    const decoded = core.decodeCode(input.value);
    if (!decoded) {
      toast('That is not a valid card code');
      return;
    }

    roll(decoded.seed, decoded.kind, false);
    state.mask = decoded.mask >>> 0;
    render();
    renderCode();
    toast('Shared card loaded');
  });

  document.getElementById('load-input').addEventListener('keydown', (event) => {
    if (event.key === 'Enter') document.getElementById('load').click();
  });

  document.getElementById('code').addEventListener('click', () => copy(renderCode(), 'Card code'));
}

document.getElementById('pool-size').textContent = String(POOL.length);
wire();
roll(randomSeed(), 'Dungeon', false);
