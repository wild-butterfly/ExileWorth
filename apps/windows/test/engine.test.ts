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
        expect(init?.body).toContain('Divine Orb');
        body = { result: ['listing'], total: 1, id: 'test-query' };
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
      new TradeClient({ fetch, userAgent: 'Vaaluation/test' }),
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
  });
});
