// Parity check: the browser preview must generate byte-identical cards to the C# plugin.
//
//   node tools/check_demo.mjs [fixtures.json]
//
// Fixtures are produced by tests/CoreTests --fixtures.

import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '..');

const fixturesPath = process.argv[2] ?? path.join(here, 'demo', 'fixtures.json');
const corePath = path.join(root, 'preview', 'src', 'core.js');
const poolPath = path.join(root, 'Data', 'squares.json');

for (const required of [fixturesPath, corePath, poolPath]) {
  if (!fs.existsSync(required)) {
    console.error(`missing: ${required}`);
    process.exit(1);
  }
}

vm.runInThisContext(fs.readFileSync(corePath, 'utf8'), { filename: corePath });
const core = globalThis.BingoCore;

const pool = JSON.parse(fs.readFileSync(poolPath, 'utf8')).squares.map((square) => ({
  id: square.id,
  text: square.text,
  cat: square.cat,
  rarity: square.rarity,
  tags: square.tags ?? [],
}));

const fixtures = JSON.parse(fs.readFileSync(fixturesPath, 'utf8')).cards;
let failures = 0;

for (const fixture of fixtures) {
  const cells = core.generate(BigInt(fixture.seed), fixture.kind, pool, fixture.cursed);
  const ids = cells.map((cell) => cell.id);
  const free = cells[12].text;

  const sameIds = ids.length === fixture.ids.length && ids.every((id, i) => id === fixture.ids[i]);
  const sameFree = free === fixture.free;

  if (!sameIds) {
    failures++;
    console.error(`FAIL ids  ${fixture.kind} seed=${fixture.seed} cursed=${fixture.cursed}`);
    ids.forEach((id, i) => {
      if (id !== fixture.ids[i]) console.error(`   [${i}] js=${id} cs=${fixture.ids[i]}`);
    });
  }

  if (!sameFree) {
    failures++;
    console.error(`FAIL free ${fixture.kind} seed=${fixture.seed}: js="${free}" cs="${fixture.free}"`);
  }

  // Cross-language share code: the plugin and the preview must emit the same string.
  const code = core.encodeCode(BigInt(fixture.seed), 0x1a5c3, fixture.kind);
  if (code !== fixture.code) {
    failures++;
    console.error(`FAIL code ${fixture.kind} seed=${fixture.seed}: js=${code} cs=${fixture.code}`);
  }
}

// Share-code round trip, including the 25-bit mask.
for (let i = 0; i < 200; i++) {
  const seed = (BigInt(Math.floor(Math.random() * 0xffffffff)) << 32n) | BigInt(Math.floor(Math.random() * 0xffffffff));
  const mask = Math.floor(Math.random() * (1 << 25));
  const kind = ['Dungeon', 'EightMan', 'Alliance', 'Pvp', 'Other'][i % 5];
  const code = core.encodeCode(seed, mask, kind);
  const decoded = core.decodeCode(code);

  if (!decoded || decoded.seed !== seed || decoded.mask !== mask || decoded.kind !== kind) {
    failures++;
    console.error(`FAIL share code round trip: ${code} -> ${JSON.stringify(decoded)}`);
    break;
  }
}

// The C# encoder writes seed little-endian with a version byte; spot-check the fixed shape.
const sample = core.encodeCode(1n, 0, 'Dungeon');
if (!sample.startsWith('RB1.') || sample.length !== 23) {
  failures++;
  console.error(`FAIL share code shape: ${sample}`);
}

if (failures > 0) {
  console.error(`\n${failures} parity failure(s)`);
  process.exit(1);
}

console.log(`parity ok: ${fixtures.length} cards, share codes round-trip`);
