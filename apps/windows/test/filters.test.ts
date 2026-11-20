import { describe, expect, it } from 'vitest';
import { parseItemText } from '@vaaluation/item-parser';
import {
  buildQuery,
  StatIndex,
  TradeClient,
  type TradeQuery,
} from '@vaaluation/trade-client';
import { windowsFilters } from '../src/filters';
import { PriceCheckEngine } from '../src/engine';

const explicit = [
  '+#% to Fire Resistance',
  '+#% to Cold Resistance',
  '+#% to Lightning Resistance',
  '+#% to all Elemental Resistances',
  '+#% to Fire and Cold Resistances',
].map((text, index) => ({ text, id: `explicit.res_${index}`, type: 'explicit' }));
const groups = [
  { label: 'Explicit', entries: explicit },
  {
    label: 'Pseudo',
    entries: [
      {
        text: '+#% total Elemental Resistance',
        id: 'pseudo.pseudo_total_elemental_resistance',
        type: 'pseudo',
      },
    ],
  },
];
function item(modifiers: string) {
  const result = parseItemText(
    `Item Class: Boots\nRarity: Rare\nTest Boots\nRawhide Boots\n--------\nItem Level: 80\n--------\n${modifiers}`,
  );
  if (!result.ok) throw new Error(result.message);
  return result.item;
}

describe('compact combined resistance filter', () => {
  it('sends only the combined minimum even when individual keys were also requested', async () => {
    const queries: TradeQuery[] = [];
    const engine = new PriceCheckEngine(
      new TradeClient({
        userAgent: 'test',
        fetch: async (url, init) => {
          const path = new URL(url).pathname;
          let body: unknown = { result: [] };
          if (path.endsWith('/data/leagues'))
            body = { result: [{ id: 'Standard', realm: 'pc' }] };
          if (path.endsWith('/data/stats')) body = { result: groups };
          if (path.endsWith('/data/items'))
            body = { result: [{ entries: [{ type: 'Rawhide Boots' }] }] };
          if (path.endsWith('/search/Standard')) {
            queries.push(JSON.parse(init?.body ?? '{}') as TradeQuery);
            body = { result: [], total: 0, id: 'combined-search' };
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
    const parsed = item(
      '+40% to Fire Resistance\n+30% to Cold Resistance\n+20% to Lightning Resistance',
    );
    const initial = await engine.check(parsed.rawText, 'Standard', []);
    const combined = initial.filters.find((filter) => filter.combinedKeys)!;
    const result = await engine.check(
      null,
      'Standard',
      [combined.key, ...combined.combinedKeys!],
      undefined,
      { [combined.key]: 81 },
    );
    expect(queries.at(-1)?.query.stats[0]?.filters).toEqual([
      { id: 'pseudo.pseudo_total_elemental_resistance', value: { min: 81 } },
    ]);
    expect(
      result.filters.filter((filter) => filter.enabled).map((filter) => filter.key),
    ).toEqual([combined.key]);
    expect(result.tradeUrl).toContain('/Standard/combined-search');
  });
  it('offers an optional total with a labelled breakdown and source keys', () => {
    const filters = windowsFilters(
      item(
        '+40% to Fire Resistance\n+30% to Cold Resistance\n+20% to Lightning Resistance',
      ),
      new StatIndex(groups),
    );
    const combined = filters.find((filter) => filter.combinedKeys)!;
    expect(combined).toMatchObject({ value: 90, min: 90, selected: false });
    expect(combined.combinedKeys).toHaveLength(3);
    expect(combined.tooltip).toContain('40% fire + 30% cold + 20% lightning');
    const query = buildQuery(
      item(
        '+40% to Fire Resistance\n+30% to Cold Resistance\n+20% to Lightning Resistance',
      ),
      filters.map((filter) => ({ ...filter, selected: filter === combined, min: 81 })),
    );
    expect(query.query.stats[0]?.filters).toEqual([
      { id: 'pseudo.pseudo_total_elemental_resistance', value: { min: 81 } },
    ]);
  });
  it('counts all-elemental and two-element rolls correctly, including penalties', () => {
    const filters = windowsFilters(
      item(
        '+10% to all Elemental Resistances\n+15% to Fire and Cold Resistances\n-5% to Lightning Resistance',
      ),
      new StatIndex(groups),
    );
    expect(filters.find((filter) => filter.combinedKeys)).toMatchObject({ value: 55 });
  });
  it('does not combine an unfamiliar or conditional resistance roll', () => {
    const filters = windowsFilters(
      item(
        '+40% to Fire Resistance\n+30% to Cold Resistance\n+20% to Lightning Resistance while on Low Life',
      ),
      new StatIndex(groups),
    );
    expect(filters.some((filter) => filter.combinedKeys)).toBe(false);
  });
  it('keeps individual filters when the live catalogue has no combined stat', () => {
    const filters = windowsFilters(
      item('+40% to Fire Resistance\n+30% to Cold Resistance'),
      new StatIndex([groups[0]!]),
    );
    expect(filters.some((filter) => filter.combinedKeys)).toBe(false);
    expect(filters).toHaveLength(2);
  });
});
