// Pure card logic, shared by the browser preview and the node parity check.
// It mirrors Core/CardGenerator.cs exactly: same PRNG, same weights, same pick order.
// Keep the two in sync; tools/check_demo.mjs verifies it against C# fixtures.

const MASK64 = (1n << 64n) - 1n;

class SplitMix64 {
  constructor(seed) {
    this.state = BigInt(seed) & MASK64;
  }

  next() {
    this.state = (this.state + 0x9E3779B97F4A7C15n) & MASK64;
    let z = this.state;
    z = ((z ^ (z >> 30n)) * 0xBF58476D1CE4E5B9n) & MASK64;
    z = ((z ^ (z >> 27n)) * 0x94D049BB133111EBn) & MASK64;
    return (z ^ (z >> 31n)) & MASK64;
  }

  nextInt(max) {
    return Number(this.next() % BigInt(max));
  }

  nextDouble() {
    return Number(this.next() >> 11n) / 9007199254740992;
  }

  shuffle(items) {
    for (let i = items.length - 1; i > 0; i--) {
      const j = this.nextInt(i + 1);
      const swap = items[i];
      items[i] = items[j];
      items[j] = swap;
    }
  }
}

const FREE_SPACES = [
  'The duty popped at all',
  'You are, in fact, inside a duty',
  'The queue timer lied to you',
  'Someone is here against their will',
  'This is a duty. Congratulations.',
  'You could be doing literally anything else',
];

const TAGS = {
  Dungeon: 'dungeon',
  EightMan: 'eightman',
  Alliance: 'alliance',
  Pvp: 'pvp',
};

function fitsDuty(square, kind) {
  if (!square.tags || square.tags.length === 0) return true;
  const tag = TAGS[kind] || '';
  return tag !== '' && square.tags.some((t) => t.toLowerCase() === tag);
}

function quotas(kind, cursed) {
  if (cursed) {
    return [
      ['Cursed', 12],
      ['Damage', 4],
      ['Social', 4],
      ['Tank', 2],
      ['Healer', 2],
    ];
  }

  if (kind === 'Alliance') {
    return [
      ['Alliance', 5],
      ['Tank', 4],
      ['Healer', 4],
      ['Damage', 4],
      ['Social', 5],
      ['Cursed', 2],
    ];
  }

  if (kind === 'Pvp') {
    return [
      ['Pvp', 6],
      ['Social', 6],
      ['Damage', 5],
      ['Healer', 3],
      ['Tank', 2],
      ['Cursed', 2],
    ];
  }

  return [
    ['Tank', 4],
    ['Healer', 4],
    ['Damage', 5],
    ['Social', 6],
    ['Loot', 2],
    ['Sprout', 2],
    ['Cursed', 1],
  ];
}

function weight(rarity) {
  if (rarity === 'Common') return 10.0;
  if (rarity === 'Uncommon') return 6.0;
  if (rarity === 'Rare') return 3.0;
  return 1.5;
}

function weightedPick(candidates, rng) {
  let total = 0;
  for (const candidate of candidates) total += weight(candidate.rarity);

  let roll = rng.nextDouble() * total;
  for (const candidate of candidates) {
    roll -= weight(candidate.rarity);
    if (roll <= 0) return candidate;
  }

  return candidates[candidates.length - 1];
}

function generate(seed, kind, pool, cursed = false) {
  const rng = new SplitMix64(seed);
  const chosen = [];
  const used = new Set();

  for (const [category, count] of quotas(kind, cursed)) {
    let candidates = pool.filter((s) => s.cat === category && fitsDuty(s, kind) && !used.has(s.id));
    for (let i = 0; i < count && candidates.length > 0; i++) {
      const pick = weightedPick(candidates, rng);
      candidates = candidates.filter((c) => c !== pick);
      used.add(pick.id);
      chosen.push(pick);
    }
  }

  if (chosen.length < 24) {
    const spare = pool.filter((s) => !used.has(s.id) && fitsDuty(s, kind));
    rng.shuffle(spare);
    for (const square of spare) {
      if (chosen.length >= 24) break;
      used.add(square.id);
      chosen.push(square);
    }
  }

  if (chosen.length < 24) {
    const spare = pool.filter((s) => !used.has(s.id));
    rng.shuffle(spare);
    for (const square of spare) {
      if (chosen.length >= 24) break;
      used.add(square.id);
      chosen.push(square);
    }
  }

  if (chosen.length !== 24) throw new Error('pool too small');

  rng.shuffle(chosen);

  const free = {
    id: 'free',
    text: FREE_SPACES[rng.nextInt(FREE_SPACES.length)],
    cat: 'Free',
    rarity: 'Common',
    tags: [],
  };

  const cells = [];
  let cursor = 0;
  for (let i = 0; i < 25; i++) cells.push(i === 12 ? free : chosen[cursor++]);
  return cells;
}

const LINES = (() => {
  const result = [];
  for (let row = 0; row < 5; row++) {
    result.push({ label: `Row ${row + 1}`, cells: [0, 1, 2, 3, 4].map((c) => row * 5 + c) });
  }
  for (let col = 0; col < 5; col++) {
    result.push({ label: `Column ${col + 1}`, cells: [0, 1, 2, 3, 4].map((r) => r * 5 + col) });
  }
  result.push({ label: 'Diagonal', cells: [0, 6, 12, 18, 24] });
  result.push({ label: 'Anti-diagonal', cells: [4, 8, 12, 16, 20] });
  return result;
})();

function completedLines(mask) {
  return LINES.filter((line) => line.cells.every((index) => (mask & (1 << index)) !== 0));
}

// ------------------------------------------------------------------ share code

const B64 = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_';

function encodeBase64Url(bytes) {
  let out = '';
  for (let i = 0; i < bytes.length; i += 3) {
    const b0 = bytes[i];
    const b1 = i + 1 < bytes.length ? bytes[i + 1] : 0;
    const b2 = i + 2 < bytes.length ? bytes[i + 2] : 0;
    const triple = (b0 << 16) | (b1 << 8) | b2;
    out += B64[(triple >> 18) & 63];
    out += B64[(triple >> 12) & 63];
    if (i + 1 < bytes.length) out += B64[(triple >> 6) & 63];
    if (i + 2 < bytes.length) out += B64[triple & 63];
  }
  return out;
}

function decodeBase64Url(text) {
  const clean = text.replace(/[^A-Za-z0-9_-]/g, '');
  const bytes = [];
  for (let i = 0; i < clean.length; i += 4) {
    const chunk = [];
    for (let j = 0; j < 4 && i + j < clean.length; j++) chunk.push(B64.indexOf(clean[i + j]));
    const triple = (chunk[0] << 18) | ((chunk[1] || 0) << 12) | ((chunk[2] || 0) << 6) | (chunk[3] || 0);
    bytes.push((triple >> 16) & 255);
    if (chunk.length > 2) bytes.push((triple >> 8) & 255);
    if (chunk.length > 3) bytes.push(triple & 255);
  }
  return bytes;
}

function encodeCode(seed, mask, kind) {
  const kindIndex = ['Other', 'Dungeon', 'EightMan', 'Alliance', 'Pvp'].indexOf(kind);
  const bytes = new Uint8Array(14);
  bytes[0] = 1;
  let value = BigInt(seed) & MASK64;
  for (let i = 0; i < 8; i++) {
    bytes[1 + i] = Number(value & 255n);
    value >>= 8n;
  }
  bytes[9] = mask & 255;
  bytes[10] = (mask >> 8) & 255;
  bytes[11] = (mask >> 16) & 255;
  bytes[12] = (mask >> 24) & 255;
  bytes[13] = kindIndex < 0 ? 0 : kindIndex;
  return 'RB1.' + encodeBase64Url(bytes);
}

function decodeCode(text) {
  if (!text || !/^RB1\./i.test(text.trim())) return null;
  const bytes = decodeBase64Url(text.trim().slice(4));
  if (bytes.length !== 14 || bytes[0] !== 1) return null;

  let seed = 0n;
  for (let i = 7; i >= 0; i--) seed = (seed << 8n) | BigInt(bytes[1 + i]);

  const mask = bytes[9] | (bytes[10] << 8) | (bytes[11] << 16) | (bytes[12] << 24);
  const kind = ['Other', 'Dungeon', 'EightMan', 'Alliance', 'Pvp'][bytes[13]] || 'Other';
  return { seed, mask: mask >>> 0, kind };
}

function report(cells, mask, seed, kind, dutyName) {
  const lines = completedLines(mask);
  const marked = cells.filter((_, i) => mask & (1 << i)).length;
  const grid = [];
  for (let row = 0; row < 5; row++) {
    let line = '';
    for (let col = 0; col < 5; col++) {
      line += mask & (1 << (row * 5 + col)) ? '[X]' : '[ ]';
    }
    grid.push(line);
  }

  const body = cells
    .map((cell, i) => (i === 12 ? null : `[${mask & (1 << i) ? 'X' : ' '}] ${cell.text}`))
    .filter(Boolean)
    .join('\n');

  return [
    `ROULETTE BINGO - ${dutyName}`,
    `${kindLabel(kind)} | ${marked}/25 squares | ${lines.length ? lines.map((l) => l.label).join(', ') : 'no bingo yet'}`,
    '',
    ...grid,
    '',
    body,
    '',
    `Card code: ${encodeCode(seed, mask, kind)}`,
  ].join('\n');
}

function kindLabel(kind) {
  return (
    {
      Dungeon: '4-player dungeon',
      EightMan: '8-player duty',
      Alliance: '24-player alliance raid',
      Pvp: 'PvP',
    }[kind] || 'duty'
  );
}

globalThis.BingoCore = {
  SplitMix64,
  FREE_SPACES,
  generate,
  completedLines,
  encodeCode,
  decodeCode,
  report,
  kindLabel,
  LINES,
};
