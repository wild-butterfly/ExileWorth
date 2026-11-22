import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { parseItemText } from '@vaaluation/item-parser';
import { PriceCheckEngine } from '../../../apps/windows/src/engine';
import { StatIndex } from '../src/stats';
import { parseStatGroups } from '../src/validate';
import { buildQuery } from '../src/query';
import { buildPriceFilters } from '../src/priceFilters';
import { TradeClient } from '../src/client';
import { BaseTypeIndex } from '../src/baseTypes';
import type { TradeQuery } from '../src/types';

// Mocked: a subset of https://www.pathofexile.com/api/trade/data/stats. The
// ids and the Wand option (5) were rechecked against the live catalogue.
const catalogue = JSON.parse(
  readFileSync(join(__dirname, 'fixtures/cluster-stats.json'), 'utf8'),
);
const stats = new StatIndex(parseStatGroups(catalogue));
const fixture = (name: string) =>
  readFileSync(join(__dirname, '../../item-parser/test/fixtures', name), 'utf8');
function item(name: string) {
  const result = parseItemText(fixture(name));
  if (!result.ok) throw new Error(result.message);
  return result.item;
}
const filtersOf = (query: TradeQuery) => query.query.stats[0]!.filters;
const baseTypes = new BaseTypeIndex([
  {
    entries: [
      { type: 'Small Cluster Jewel' },
      { type: 'Medium Cluster Jewel' },
      { type: 'Large Cluster Jewel' },
    ],
  },
]);
const search = (name: string) => {
  const parsed = item(name);
  return buildQuery(parsed, buildPriceFilters(parsed, stats), { baseTypes });
};

const LARGE = [
  { id: 'enchant.stat_3086156145', value: { min: 8, max: 8 } },
  { id: 'enchant.stat_4079888060', value: { min: 2, max: 2 } },
  { id: 'enchant.stat_3948993189', value: { option: 5 } },
  { id: 'explicit.stat_2669029667', value: { min: 3 } },
];

describe('cluster jewels with copied explanations', () => {
  it.each([
    ['plain', 'cluster-jewel-magic.txt'],
    ['advanced, both explanations', 'cluster-jewel-large-advanced.txt'],
    [
      'plain, suffixed explanations and split descriptions',
      'cluster-jewel-large-plain-multiline.txt',
    ],
  ])('searches the Large Cluster Jewel of the Prism (%s)', (_format, name) => {
    const query = search(name);
    expect(query.query.type).toBe('Large Cluster Jewel');
    expect(query.query.name).toBeUndefined();
    expect(filtersOf(query)).toEqual(LARGE);
  });

  it.each([
    ['Medium', 'cluster-jewel.txt', 'cluster-jewel-medium-advanced.txt'],
    ['Small', 'cluster-jewel-small.txt', 'cluster-jewel-small-both-reminders.txt'],
    ['Small', 'cluster-jewel-small.txt', 'cluster-jewel-small-advanced.txt'],
  ])(
    'searches a %s cluster jewel identically in plain and advanced copies',
    (size, plain, other) => {
      const expected = search(plain);
      expect(expected.query.type).toBe(`${size} Cluster Jewel`);
      expect(search(other)).toEqual(expected);
    },
  );

  it('still reports a genuinely unsupported cluster modifier', () => {
    const source = fixture('cluster-jewel-large-advanced.txt').replace(
      '2 Added Passive Skills are Jewel Sockets',
      '2 Added Passive Skills are Imaginary Things',
    );
    const parsed = parseItemText(source);
    if (!parsed.ok) throw new Error(parsed.message);
    expect(() => buildPriceFilters(parsed.item, stats)).toThrow(
      '2 Added Passive Skills are Imaginary Things',
    );
  });

  it('sends deselections and edited minimums, and links the search it sent', async () => {
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
            body = { result: [{ entries: [{ type: 'Large Cluster Jewel' }] }] };
          if (path.endsWith('/search/Standard')) {
            queries.push(JSON.parse(init?.body ?? '{}') as TradeQuery);
            body = { id: `search-${queries.length}`, total: 0, result: [] };
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
    const first = await engine.check(
      fixture('cluster-jewel-large-advanced.txt'),
      'Standard',
      null,
    );
    expect(queries[0]!.query.type).toBe('Large Cluster Jewel');
    expect(filtersOf(queries[0]!)).toEqual(LARGE);
    expect(first.tradeUrl).toBe(
      'https://www.pathofexile.com/trade/search/Standard/search-1',
    );

    const sockets = first.filters.find((f) => f.label.includes('Jewel Sockets'))!;
    const resistance = first.filters.find((f) =>
      f.label.includes('Elemental Resistances'),
    )!;
    const kept = first.filters.filter((f) => f.key !== sockets.key).map((f) => f.key);
    const second = await engine.check(null, 'Standard', kept, undefined, {
      [resistance.key]: 2,
    });
    expect(filtersOf(queries[1]!)).toEqual([
      { id: 'enchant.stat_3086156145', value: { min: 8, max: 8 } },
      { id: 'enchant.stat_3948993189', value: { option: 5 } },
      { id: 'explicit.stat_2669029667', value: { min: 2 } },
    ]);
    // The Trade site link opens exactly the edited search that was sent.
    expect(second.tradeUrl).toBe(
      'https://www.pathofexile.com/trade/search/Standard/search-2',
    );
  });
});
