import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { parseItemText } from '@vaaluation/item-parser';
import { PriceCheckEngine } from '../../../apps/windows/src/engine';
import { StatIndex } from '../src/stats';
import { parseStatGroups } from '../src/validate';
import { buildQuery, relaxWeakest } from '../src/query';
import {
  applyMinimumPreset,
  buildPriceFilters,
  matchingMinimumPreset,
} from '../src/priceFilters';
import { TradeClient } from '../src/client';
import { BaseTypeIndex } from '../src/baseTypes';
import type { TradeQuery } from '../src/types';

const text = readFileSync(
  join(__dirname, '../../item-parser/test/fixtures/cluster-jewel.txt'),
  'utf8',
);
// Subset of https://www.pathofexile.com/api/trade/data/stats, read 2026-11-22.
const catalogue = JSON.parse(
  readFileSync(join(__dirname, 'fixtures/cluster-stats.json'), 'utf8'),
);
const groups = parseStatGroups(catalogue);
const stats = new StatIndex(groups);
function parsed(source = text) {
  const result = parseItemText(source);
  if (!result.ok) throw new Error(result.message);
  return result.item;
}
const queryFilters = (query: TradeQuery) => query.query.stats[0]!.filters;

describe('cluster jewel modifier searches', () => {
  it.each([
    '(Added Passive Skills are never considered to be in Radius by other Jewels) (enchant)',
    'Added Passive Skills are never considered to be in Radius by other Jewels',
    '(Added Passive Skills are never considered to be in Radius by other Jewels.)',
    '(Added Passive Skills are never considered to be in Radius by other Jewels). (enchant)',
    '(Added Passive Skills are never considered to be in Radius by other Jewels) (implicit)',
    '(Added Passive Skills are never considered to be in Radius by other Jewels)'.replaceAll(
      ' ',
      '\u00a0',
    ),
    '(Added Passive Skills are never considered to be in Radius by other Jewels)'.replace(
      'Radius',
      '\u200bRadius',
    ),
  ])('retains all real filters when the radius reminder is copied as %s', (reminder) => {
    const source = readFileSync(
      join(__dirname, '../../item-parser/test/fixtures/cluster-jewel-small-advanced.txt'),
      'utf8',
    );
    const original =
      '(Added Passive Skills are never considered to be in Radius by other Jewels)';
    const item = parsed(source.replace(original, reminder));
    const expected = buildQuery(parsed(source), buildPriceFilters(parsed(source), stats));
    expect(buildQuery(item, buildPriceFilters(item, stats))).toEqual(expected);
    // The query boundary must also handle an already parsed reminder.
    if (item.kind !== 'equipment') throw new Error('Expected cluster jewel');
    const stale = {
      ...item,
      modifiers: [...item.modifiers, { text: reminder, type: 'explicit' as const }],
    };
    expect(buildQuery(stale, buildPriceFilters(stale, stats))).toEqual(expected);
  });
  it('ignores the reported cluster radius reminder and unnamed advanced headings while retaining every real filter', () => {
    const plain = parsed(
      readFileSync(
        join(__dirname, '../../item-parser/test/fixtures/cluster-jewel-small.txt'),
        'utf8',
      ),
    );
    const advanced = parsed(
      readFileSync(
        join(
          __dirname,
          '../../item-parser/test/fixtures/cluster-jewel-small-advanced.txt',
        ),
        'utf8',
      ),
    );
    if (advanced.kind !== 'equipment') throw new Error('Expected cluster jewel');
    expect(advanced.unknownLines).toContain(
      '(Added Passive Skills are never considered to be in Radius by other Jewels)',
    );
    expect(advanced.modifiers).toHaveLength(4);
    expect(advanced.modifiers.map((modifier) => modifier.type)).toEqual([
      'enchant',
      'enchant',
      'explicit',
      'explicit',
    ]);
    const baseTypes = new BaseTypeIndex([{ entries: [{ type: 'Small Cluster Jewel' }] }]);
    expect(
      buildQuery(advanced, buildPriceFilters(advanced, stats), { baseTypes }),
    ).toEqual(buildQuery(plain, buildPriceFilters(plain, stats), { baseTypes }));
  });
  it('matches both rolled stats in the Shining Small Cluster Jewel screenshot without inventing a socket filter', () => {
    const source = readFileSync(
      join(__dirname, '../../item-parser/test/fixtures/cluster-jewel-small.txt'),
      'utf8',
    );
    const item = parsed(source);
    const rows = buildPriceFilters(item, stats);
    const baseTypes = new BaseTypeIndex([{ entries: [{ type: 'Small Cluster Jewel' }] }]);
    const full = buildQuery(item, rows, { baseTypes });
    expect(full.query.type).toBe('Small Cluster Jewel');
    expect(full.query.name).toBeUndefined();
    expect(queryFilters(full)).toEqual([
      { id: 'enchant.stat_3086156145', value: { min: 3, max: 3 } },
      { id: 'enchant.stat_3948993189', value: { option: 47 } },
      { id: 'explicit.stat_2474836297', value: { min: 5 } },
      { id: 'explicit.stat_2643685329', value: { min: 4 } },
    ]);
    const selected = rows.map((row) => ({
      ...row,
      selected: row.statId.startsWith('explicit.'),
    }));
    expect(queryFilters(buildQuery(item, selected, { baseTypes }))).toEqual([
      { id: 'explicit.stat_2474836297', value: { min: 5 } },
      { id: 'explicit.stat_2643685329', value: { min: 4 } },
    ]);
    const broad = applyMinimumPreset(rows, 20);
    expect(queryFilters(buildQuery(item, broad, { baseTypes }))).toEqual([
      { id: 'enchant.stat_3086156145', value: { min: 3, max: 3 } },
      { id: 'enchant.stat_3948993189', value: { option: 47 } },
      { id: 'explicit.stat_2474836297', value: { min: 4 } },
      { id: 'explicit.stat_2643685329', value: { min: 3.2 } },
    ]);
  });

  it('matches multi-line trap/mine passive types as a single categorical filter', () => {
    const source = readFileSync(
      join(__dirname, '../../item-parser/test/fixtures/cluster-jewel-small.txt'),
      'utf8',
    ).replace(
      'Added Small Passive Skills grant: +15% to Cold Resistance (enchant)',
      'Added Small Passive Skills grant: 12% increased Trap Damage (enchant)\nAdded Small Passive Skills grant: 12% increased Mine Damage (enchant)',
    );
    const item = parsed(source);
    const rows = buildPriceFilters(item, stats);
    expect(rows).toHaveLength(4);
    expect(rows[1]).toMatchObject({
      statId: 'enchant.stat_3948993189',
      option: 33,
      value: null,
    });
  });

  it('matches the magic Large Cluster Jewel example and its specific small-passive resistance stat', () => {
    const source = readFileSync(
      join(__dirname, '../../item-parser/test/fixtures/cluster-jewel-magic.txt'),
      'utf8',
    );
    const item = parsed(source);
    const rows = buildPriceFilters(item, stats);
    const baseTypes = new BaseTypeIndex([{ entries: [{ type: 'Large Cluster Jewel' }] }]);
    const query = buildQuery(item, rows, { baseTypes });
    expect(query.query.type).toBe('Large Cluster Jewel');
    expect(query.query.name).toBeUndefined();
    expect(queryFilters(query)).toEqual([
      { id: 'enchant.stat_3086156145', value: { min: 8, max: 8 } },
      { id: 'enchant.stat_4079888060', value: { min: 2, max: 2 } },
      { id: 'enchant.stat_3948993189', value: { option: 5 } },
      { id: 'explicit.stat_2669029667', value: { min: 3 } },
    ]);
    expect(relaxWeakest(rows)).toBeNull();
    const selected = rows.map((row) => ({
      ...row,
      selected: row.statId === 'explicit.stat_2669029667',
    }));
    expect(queryFilters(buildQuery(item, selected, { baseTypes }))).toEqual([
      { id: 'explicit.stat_2669029667', value: { min: 3 } },
    ]);
    expect(
      queryFilters(
        buildQuery(
          item,
          selected.map((row) => ({ ...row, min: row.selected ? 2 : row.min })),
          { baseTypes },
        ),
      ),
    ).toEqual([{ id: 'explicit.stat_2669029667', value: { min: 2 } }]);
  });

  it('keeps counts exact on normal cluster bases as well as magic affixed names', () => {
    const source = readFileSync(
      join(__dirname, '../../item-parser/test/fixtures/cluster-jewel-magic.txt'),
      'utf8',
    )
      .replace('Rarity: Magic', 'Rarity: Normal')
      .replace('Large Cluster Jewel of the Prism', 'Large Cluster Jewel');
    const rows = buildPriceFilters(parsed(source), stats);
    expect(rows[0]).toMatchObject({
      selected: true,
      min: 8,
      max: 8,
      exact: true,
      relaxable: false,
    });
    expect(rows[1]).toMatchObject({
      selected: true,
      min: 2,
      max: 2,
      exact: true,
      relaxable: false,
    });
  });
  it('preserves both colon-containing modifier sections and ignores usage text', () => {
    const item = parsed();
    expect(item.kind).toBe('equipment');
    if (item.kind !== 'equipment') throw new Error('Expected equipment');
    expect(item.baseType).toBe('Medium Cluster Jewel');
    expect(item.modifiers).toHaveLength(6);
    expect(item.modifiers.map((mod) => mod.type)).toEqual([
      'enchant',
      'enchant',
      'enchant',
      'explicit',
      'explicit',
      'explicit',
    ]);
    expect(item.modifiers.at(-1)?.text).toBe('1 Added Passive Skill is Life from Death');
  });

  it('sends all six properties, exact passive/socket counts, the minion-life option and the notable', () => {
    const item = parsed();
    const rows = buildPriceFilters(item, stats);
    expect(rows).toHaveLength(6);
    expect(rows.every((row) => row.selected)).toBe(true);
    expect(queryFilters(buildQuery(item, rows))).toEqual([
      { id: 'enchant.stat_3086156145', value: { min: 4, max: 4 } },
      { id: 'enchant.stat_4079888060', value: { min: 1, max: 1 } },
      { id: 'enchant.stat_3948993189', value: { option: 30 } },
      { id: 'explicit.stat_4036575250', value: { min: 2 } },
      { id: 'explicit.stat_2618549697', value: { min: 25 } },
      { id: 'explicit.stat_2337273077' },
    ]);
    expect(rows[2]!.value).toBeNull();
    expect(rows[5]!.value).toBeNull();
    expect(relaxWeakest(rows)).toBeNull();
  });

  it('handles plain-copy cluster descriptions without enchant suffixes', () => {
    const item = parsed(text.replaceAll(' (enchant)', ''));
    const filters = queryFilters(buildQuery(item, buildPriceFilters(item, stats)));
    expect(filters[0]!.id).toBe('enchant.stat_3086156145');
    expect(filters[1]!.id).toBe('enchant.stat_4079888060');
    expect(filters[2]).toEqual({ id: 'enchant.stat_3948993189', value: { option: 30 } });
  });

  it('presets lower rolls while preserving counts, passive type and notable', () => {
    const item = parsed();
    const rows = applyMinimumPreset(buildPriceFilters(item, stats), 20);
    expect(matchingMinimumPreset(rows)).toBe(20);
    expect(queryFilters(buildQuery(item, rows))).toEqual([
      { id: 'enchant.stat_3086156145', value: { min: 4, max: 4 } },
      { id: 'enchant.stat_4079888060', value: { min: 1, max: 1 } },
      { id: 'enchant.stat_3948993189', value: { option: 30 } },
      { id: 'explicit.stat_4036575250', value: { min: 1.6 } },
      { id: 'explicit.stat_2618549697', value: { min: 20 } },
      { id: 'explicit.stat_2337273077' },
    ]);
  });

  it('rejects incomplete cluster matching rather than returning a base-only price', () => {
    const incomplete = new StatIndex(
      groups.map((g) => ({
        ...g,
        entries: g.entries.filter((e) => !e.text.includes('Life from Death')),
      })),
    );
    expect(() => buildPriceFilters(parsed(), incomplete)).toThrow('Life from Death');
    expect(() =>
      buildPriceFilters(parsed(text.split('--------\nAdds')[0]!), stats),
    ).toThrow('No cluster-jewel modifiers');
  });

  it('keeps the full query for zero matches and sends edited values through the Windows worker engine', async () => {
    const queries: TradeQuery[] = [];
    const engine = new PriceCheckEngine(
      new TradeClient({
        userAgent: 'test',
        fetch: async (url, init) => {
          const path = new URL(url).pathname;
          let body: unknown = { result: [] };
          if (path.endsWith('/data/leagues'))
            body = { result: [{ id: 'Standard', realm: 'pc' }] };
          if (path.endsWith('/data/stats')) body = catalogue;
          if (path.endsWith('/data/items'))
            body = { result: [{ entries: [{ type: 'Medium Cluster Jewel' }] }] };
          if (path.endsWith('/search/Standard')) {
            queries.push(JSON.parse(init?.body ?? '{}') as TradeQuery);
            body = { id: 'cluster-search', total: 0, result: [] };
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
    const first = await engine.check(text, 'Standard', null);
    expect(queries).toHaveLength(1);
    expect(queryFilters(queries[0]!)).toHaveLength(6);
    const effect = first.filters.find((f) => f.label.includes('increased Effect'))!;
    const passives = first.filters.find((f) => f.label.startsWith('Adds '))!;
    expect(passives.exact).toBe(true);
    await engine.check(
      null,
      'Standard',
      first.filters.map((f) => f.key),
      undefined,
      { [effect.key]: 20, [passives.key]: 5 },
    );
    const edited = queryFilters(queries[1]!);
    expect(edited.find((f) => f.id === 'explicit.stat_2618549697')?.value).toEqual({
      min: 20,
    });
    expect(edited.find((f) => f.id === 'enchant.stat_3086156145')?.value).toEqual({
      min: 5,
      max: 5,
    });
    expect(edited.find((f) => f.id === 'enchant.stat_3948993189')?.value).toEqual({
      option: 30,
    });
    expect(edited.some((f) => f.id === 'explicit.stat_2337273077')).toBe(true);
  });
});
