import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { parseItemText } from '@vaaluation/item-parser';
import { BaseTypeIndex, parseItemCatalog } from '../src/baseTypes';
import { StatIndex } from '../src/stats';
import { parseStatGroups } from '../src/validate';
import { buildQuery, relaxWeakest } from '../src/query';
import { applyMinimumPreset, buildPriceFilters } from '../src/priceFilters';
import { TradeClient } from '../src/client';
import type { TradeQuery } from '../src/types';
import { PriceCheckEngine } from '../../../apps/windows/src/engine';

// Small subsets of the official /api/trade/data/items and /data/stats catalogues.
const items = JSON.parse(
  readFileSync(join(__dirname, 'fixtures/catalogue-items.json'), 'utf8'),
);
const catalogue = JSON.parse(
  readFileSync(join(__dirname, 'fixtures/catalogue-stats.json'), 'utf8'),
);
const baseTypes = new BaseTypeIndex(parseItemCatalog(items));
const stats = new StatIndex(parseStatGroups(catalogue));
function fixture(name: string) {
  return readFileSync(join(__dirname, '../../item-parser/test/fixtures', name), 'utf8');
}
const ankh = fixture('viscera-golden-ankh.txt');
const egg = fixture('viscera-arachnid-egg-sac.txt');
function parsed(text: string) {
  const result = parseItemText(text);
  if (!result.ok) throw new Error(result.message);
  return result.item;
}
function query(text: string) {
  const item = parsed(text);
  return buildQuery(item, buildPriceFilters(item, stats), { baseTypes });
}

describe('catalogue item names and rolled modifiers', () => {
  it('honours a declared explicit group rather than inferring an implicit Viscera stat', () => {
    const source = fixture('viscera-scholars-arm-advanced.txt').replace(
      '{ Implicit Modifier }',
      '{ Explicit Modifier }',
    );
    expect(query(source).query.stats[0]!.filters).toEqual([
      { id: 'explicit.stat_328541901', value: { min: 27 } },
    ]);
  });
  it('searches the reported Scholar’s Arm advanced-copy format without treating its heading as a stat', () => {
    const source = fixture('viscera-scholars-arm-advanced.txt');
    const item = parsed(source);
    expect(item).toMatchObject({
      kind: 'catalogue',
      name: "Scholar's Arm",
      modifiers: [{ text: '+27 to Intelligence', type: 'implicit' }],
    });
    if (item.kind !== 'catalogue') throw new Error('Expected catalogue item');
    expect(item.modifiers).toHaveLength(1);
    expect(query(source).query).toMatchObject({
      type: "Scholar's Arm",
      stats: [{ filters: [{ id: 'implicit.stat_328541901', value: { min: 27 } }] }],
    });
  });
  it.each([
    [
      ankh,
      'Golden Ankh',
      'Raised Zombies have 32% increased maximum Life',
      'implicit.stat_927817294',
      32,
    ],
    [
      egg,
      'Arachnid Egg Sac',
      'Minions created Recently have 12% increased Attack and Cast Speed',
      'implicit.stat_2510300854',
      12,
    ],
  ] as const)(
    'searches the exact catalogue type and implicit roll for %s',
    (text, name, label, id, minimum) => {
      const item = parsed(text);
      expect(item).toMatchObject({
        kind: 'catalogue',
        name,
        modifiers: [{ text: label, type: 'implicit' }],
      });
      if (item.kind !== 'catalogue') throw new Error('Expected catalogue item');
      expect(item.modifiers).toHaveLength(1);
      expect(item.unknownLines.some((line) => line.includes('Wickerman'))).toBe(true);
      const rows = buildPriceFilters(item, stats);
      expect(rows).toHaveLength(1);
      expect(rows[0]).toMatchObject({ selected: true, min: minimum, statId: id });
      expect(relaxWeakest(rows)).toBeNull();
      expect(query(text).query).toMatchObject({
        type: name,
        stats: [{ type: 'and', filters: [{ id, value: { min: minimum } }] }],
      });
    },
  );

  it('handles the name-only header format and preserves a missing stat suffix for Viscera', () => {
    expect(query(ankh.replace('Item Class: Viscera\n', '')).query.type).toBe(
      'Golden Ankh',
    );
    expect(query(ankh.replace(' (implicit)', '')).query.stats[0]!.filters[0]!.id).toBe(
      'implicit.stat_927817294',
    );
    expect(
      query(
        ankh.replace('Item Class: Viscera\n', 'Item Class: Viscera\nRarity: Normal\n'),
      ).query.stats[0]!.filters[0],
    ).toEqual({ id: 'implicit.stat_927817294', value: { min: 32 } });
  });

  it('keeps multiple rolled stats and sends the lowered preset values', () => {
    const item = parsed(
      ankh.replace(
        'Raised Zombies have 32% increased maximum Life (implicit)',
        'Raised Zombies have 32% increased maximum Life (implicit)\nMinions created Recently have 12% increased Attack and Cast Speed (implicit)',
      ),
    );
    const rows = applyMinimumPreset(buildPriceFilters(item, stats), 10);
    expect(buildQuery(item, rows, { baseTypes }).query.stats[0]!.filters).toEqual([
      { id: 'implicit.stat_927817294', value: { min: 28.8 } },
      { id: 'implicit.stat_2510300854', value: { min: 10.8 } },
    ]);
  });

  it('resolves other catalogue categories and unique names without special-casing item names', () => {
    const index = new BaseTypeIndex([
      {
        entries: [
          { type: 'Storming Eshgraft' },
          { type: 'Lavish Wombgift' },
          { type: 'Coral Forest Chart' },
        ],
      },
    ]);
    for (const name of ['Storming Eshgraft', 'Lavish Wombgift', 'Coral Forest Chart']) {
      const item = parsed(`Item Class: Other\n${name}\n--------\nItem Level: 80`);
      expect(buildQuery(item, [], { baseTypes: index }).query.type).toBe(name);
    }
    const unique = parsed('Item Class: Belts\nHeadhunter\n--------\nItem Level: 80');
    expect(buildQuery(unique, [], { baseTypes }).query).toMatchObject({
      name: 'Headhunter',
      type: 'Leather Belt',
    });
  });

  it('rejects unknown or partial identities and unsupported rolls instead of pricing a different item', () => {
    const unsupported = parsed(ankh.replace('Golden Ankh', 'Golden Ankh of Something'));
    expect(() => buildQuery(unsupported, [], { baseTypes })).toThrow(
      'not an exact item name',
    );
    expect(() => buildQuery(parsed(ankh), [])).toThrow('not an exact item name');
    expect(() =>
      buildPriceFilters(
        parsed(
          ankh.replace(
            'Raised Zombies have 32% increased maximum Life',
            'Minions have 99% unsupported Mystery Power',
          ),
        ),
        stats,
      ),
    ).toThrow('not supported yet');
    expect(parseItemText('Raised Zombies have #% increased maximum Life').ok).toBe(false);
  });

  it('keeps colon-containing rolled stats in a rarityless modifier section', () => {
    const item = parsed(
      ankh.replace(
        'Raised Zombies have 32% increased maximum Life (implicit)',
        'Minions also gain: +12 to Mystery Power (implicit)',
      ),
    );
    expect(item.kind).toBe('catalogue');
    if (item.kind !== 'catalogue') throw new Error('Expected catalogue item');
    expect(item.modifiers[0]?.text).toBe('Minions also gain: +12 to Mystery Power');
  });

  it('passes the edited stat minimum and item identity through the Windows engine without relaxing zero matches', async () => {
    const searches: TradeQuery[] = [];
    const engine = new PriceCheckEngine(
      new TradeClient({
        userAgent: 'test',
        fetch: async (url, init) => {
          const path = new URL(url).pathname;
          let body: unknown = { result: [] };
          if (path.endsWith('/data/leagues'))
            body = { result: [{ id: 'Covens', realm: 'pc' }] };
          if (path.endsWith('/data/items')) body = items;
          if (path.endsWith('/data/stats')) body = catalogue;
          if (path.endsWith('/search/Covens')) {
            searches.push(JSON.parse(init?.body ?? '{}') as TradeQuery);
            body = { id: 'ankh-search', total: 0, result: [] };
          }
          return {
            ok: true,
            status: 200,
            headers: { get: () => null },
            text: async () => JSON.stringify(body),
          };
        },
      }),
    );
    const first = await engine.check(ankh, 'Covens', null);
    expect(searches).toHaveLength(1);
    expect(first.itemName).toBe('Golden Ankh');
    const roll = first.filters[0]!;
    await engine.check(null, 'Covens', [roll.key], undefined, { [roll.key]: 28 });
    expect(searches[1]!.query).toMatchObject({
      type: 'Golden Ankh',
      stats: [{ filters: [{ id: 'implicit.stat_927817294', value: { min: 28 } }] }],
    });
    expect(searches[1]!.query.name).toBeUndefined();
  });
});
