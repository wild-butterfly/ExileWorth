import { describe, expect, it } from 'vitest';
import { TradeClient, type FetchLike } from '@vaaluation/trade-client';
import { PriceCheckEngine } from '../src/engine';

describe('Windows price check', () => {
  it('prices a copied item in Covens when the saved league is gone', async () => {
    const calls: string[] = [];
    const fetch: FetchLike = async (url, init) => {
      const path = new URL(url).pathname;
      calls.push(path);
      let body: unknown = { result: [] };
      if (path.endsWith('/data/leagues')) {
        body = {
          result: [
            { id: 'Standard', realm: 'pc' },
            { id: 'Covens', realm: 'xbox' },
            { id: 'Covens', realm: 'pc' },
          ],
        };
      } else if (path.endsWith('/data/stats')) {
        body = { result: [{ entries: [] }] };
      } else if (path.endsWith('/data/items')) {
        body = { result: [{ entries: [{ type: 'Divine Orb' }] }] };
      } else if (path.endsWith('/search/Covens')) {
        const query = JSON.parse(init?.body ?? '{}');
        expect(query.query.type).toBe('Divine Orb');
        expect(query.sort).toEqual({ price: 'asc' });
        if (query.query.filters) {
          expect(query.query.filters.trade_filters.filters.price.option).toBe('chaos');
          body = { result: ['cheap', 'dear'], total: 2, id: 'filtered-query' };
        } else {
          body = { result: ['listing'], total: 1, id: 'test-query' };
        }
      } else if (path.endsWith('/fetch/cheap,dear')) {
        body = {
          result: [
            {
              id: 'dear',
              item: {},
              listing: {
                account: { name: 'B' },
                price: { amount: 44, currency: 'chaos' },
              },
            },
            {
              id: 'cheap',
              item: {},
              listing: {
                account: { name: 'A' },
                price: { amount: 42, currency: 'chaos' },
              },
            },
          ],
        };
      } else if (path.endsWith('/fetch/listing')) {
        body = {
          result: [
            {
              id: 'listing',
              item: {},
              listing: {
                indexed: '2026-11-14T00:00:00Z',
                account: { name: 'A seller' },
                price: { amount: 42, currency: 'chaos' },
              },
            },
          ],
        };
      }
      return {
        ok: true,
        status: 200,
        headers: { get: () => null },
        text: async () => JSON.stringify(body),
      };
    };
    const engine = new PriceCheckEngine(
      new TradeClient({ fetch, userAgent: 'ExileWorth/test' }),
    );
    const result = await engine.check(
      'Item Class: Stackable Currency\r\nRarity: Currency\r\nDivine Orb',
      'Allflame',
      null,
    );
    expect(result.selected).toBe('Covens');
    expect(result.listings).toMatchObject([{ amount: 42, currency: 'chaos' }]);
    expect(result.tradeUrl).toBe(
      'https://www.pathofexile.com/trade/search/Covens/test-query',
    );
    expect(calls).toContain('/api/trade/search/Covens');
    const filtered = await engine.check(null, 'Covens', [], 'chaos');
    expect(filtered.listings.map(({ amount }) => amount)).toEqual([42, 44]);
    expect(filtered.tradeUrl).toContain('/Covens/filtered-query');
  });
});

describe('editable minimums', () => {
  it('sends edited stat and weapon minimums and retains them for later searches', async () => {
    const queries: ReturnType<typeof JSON.parse>[] = [];
    const fetch: FetchLike = async (url, init) => {
      const path = new URL(url).pathname;
      let body: unknown = { result: [] };
      if (path.endsWith('/data/leagues'))
        body = { result: [{ id: 'Standard', realm: 'pc' }] };
      if (path.endsWith('/data/items'))
        body = { result: [{ entries: [{ type: 'Driftwood Wand' }] }] };
      if (path.endsWith('/data/stats'))
        body = {
          result: [
            {
              label: 'Explicit',
              entries: [
                { id: 'explicit.life', text: '+# to maximum Life', type: 'explicit' },
              ],
            },
          ],
        };
      if (path.endsWith('/search/Standard')) {
        queries.push(JSON.parse(init?.body ?? '{}'));
        body = { result: [], total: 0, id: 'edited' };
      }
      return {
        ok: true,
        status: 200,
        headers: { get: () => null },
        text: async () => JSON.stringify(body),
      };
    };
    const engine = new PriceCheckEngine(new TradeClient({ fetch, userAgent: 'test' }));
    const initial = await engine.check(
      'Item Class: Wands\nRarity: Rare\nTest Wand\nDriftwood Wand\n--------\nPhysical Damage: 10-20\nAttacks per Second: 1.50\n--------\n+80 to maximum Life',
      'Standard',
      [],
    );
    const life = initial.filters.find((f) => f.label.includes('maximum Life'))!;
    const dps = initial.filters.find((f) => f.key === 'property:pdps')!;
    expect(life.value).toBe(80);
    expect(dps.value).toBe(22.5);
    const edited = await engine.check(null, 'Standard', [life.key, dps.key], undefined, {
      [life.key]: 70,
      [dps.key]: 20.25,
    });
    expect(queries.at(-1).query.stats[0].filters).toContainEqual({
      id: 'explicit.life',
      value: { min: 70 },
    });
    expect(queries.at(-1).query.filters.weapon_filters.filters.pdps.min).toBe(20.25);
    expect(edited.filters.find((f) => f.key === life.key)).toMatchObject({
      value: 80,
      min: 70,
    });
    expect(edited.tradeUrl).toContain('/Standard/edited');
    await engine.check(null, 'Standard', [life.key, dps.key]);
    expect(queries.at(-1).query.stats[0].filters[0].value.min).toBe(70);
  });
});
