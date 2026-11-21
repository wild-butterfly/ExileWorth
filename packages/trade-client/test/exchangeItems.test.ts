import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { parseItemText } from '@vaaluation/item-parser';
import {
  ExchangeItemIndex,
  exchangeQuantity,
  formatExchangeAmount,
} from '../src/exchangeItems';
import { TradeClient } from '../src/client';
import { PriceCheckEngine } from '../../../apps/windows/src/engine';

const catalogue = JSON.parse(
  readFileSync(join(__dirname, 'fixtures/exchange-items.json'), 'utf8'),
);
function item(name: string, count = 20) {
  const parsed = parseItemText(
    `Item Class: Stackable Currency\nRarity: Currency\n${name}\n--------\nStack Size: ${count}/100`,
  );
  if (!parsed.ok) throw new Error(parsed.message);
  return parsed.item;
}
function offer(give = 'betrayal-thread', amount = 4, chaos = 1, stock = 100) {
  return {
    listing: {
      account: { name: 'BulkSeller', online: { league: 'Covens' } },
      offers: [
        {
          exchange: { currency: 'chaos', amount: chaos },
          item: { currency: give, amount, stock },
        },
      ],
    },
  };
}
function setup(result: Record<string, unknown> = { seller: offer() }) {
  let now = 1000;
  const requests: { path: string; body: unknown }[] = [];
  const client = new TradeClient({
    userAgent: 'test',
    now: () => now,
    fetch: async (url, init) => {
      const path = new URL(url).pathname;
      const body = init?.body ? JSON.parse(init.body) : null;
      requests.push({ path, body });
      let response: unknown;
      if (path.endsWith('/data/static')) response = catalogue;
      else if (path.endsWith('/data/leagues'))
        response = { result: [{ id: 'Covens', realm: 'pc' }] };
      else if (path.endsWith('/exchange/Covens')) response = { id: 'bulk-query', result };
      else throw new Error(`Unexpected request: ${path}`);
      return {
        ok: true,
        status: 200,
        headers: { get: () => null },
        text: async () => JSON.stringify(response),
      };
    },
  });
  return {
    client,
    requests,
    advance: (ms: number) => {
      now += ms;
    },
  };
}

describe('exchange item quotes', () => {
  it('discovers every named commodity category from the official catalogue', () => {
    const index = new ExchangeItemIndex(catalogue);
    for (const name of [
      'Betrayal Thread',
      'Divine Orb',
      'Scorched Fossil',
      'Deafening Essence of Greed',
      'The Doctor',
      'Golden Oil',
      'Turbulent Catalyst',
      'Astragali',
      'Runegraft of the River',
    ]) {
      expect(index.resolve(item(name))?.name).toBe(name);
    }
    expect(index.resolve(item(' betrayal   thread '))?.id).toBe('betrayal-thread');
    expect(index.resolve(item('Betrayal Thread of Something'))).toBeNull();
  });

  it('learns future entries without app code changes and refuses ambiguous identities', () => {
    const index = new ExchangeItemIndex({
      result: [
        {
          entries: [
            { id: 'future', text: 'New Exchange Token' },
            { id: 'one', text: 'Ambiguous Token' },
            { id: 'two', text: 'Ambiguous Token' },
            { id: 'sep', text: 'Divider' },
          ],
        },
      ],
    });
    expect(index.resolve(item('New Exchange Token'))?.id).toBe('future');
    expect(index.resolve(item('Ambiguous Token'))).toBeNull();
    expect(index.resolve(item('Divider'))).toBeNull();
    expect(() => new ExchangeItemIndex({ error: 'offline' })).toThrow(
      'catalogue could not be read',
    );
  });

  it('keeps rolled gear on its stat-aware trade search even with a colliding catalogue name', () => {
    const parsed = parseItemText(
      'Item Class: Jewels\nRarity: Rare\nDivine Orb\nMedium Cluster Jewel\n--------\nAdds 4 Passive Skills (enchant)',
    );
    if (!parsed.ok) throw new Error(parsed.message);
    expect(new ExchangeItemIndex(catalogue).resolve(parsed.item)).toBeNull();
  });

  it('calculates Chaos per item and stack, and links to the exchange rather than item trade', async () => {
    const { client, requests } = setup();
    const quote = await client.quoteExchangeItem(item('Betrayal Thread'), 'Covens');
    expect(quote).toMatchObject({
      source: 'bulk-exchange',
      perItemChaos: 0.25,
      quantity: 20,
      stackChaos: 5,
      updatedAt: 1000,
      url: 'https://www.pathofexile.com/trade/exchange/Covens/bulk-query',
    });
    expect(requests[1]!.body).toMatchObject({
      query: { have: ['chaos'], want: ['betrayal-thread'] },
    });
    expect(quote?.offers[0]).toMatchObject({ stock: 100, giveAmount: 4 });
  });

  it('reuses catalogue data with icons and keeps the original check time on cached quotes', async () => {
    const { client, requests, advance } = setup();
    await client.quoteExchangeItem(item('Betrayal Thread'), 'Covens');
    advance(120_000);
    await client.getCurrencyIcons();
    const cached = await client.quoteExchangeItem(item('Betrayal Thread', 40), 'Covens');
    expect(cached).toMatchObject({ updatedAt: 1000, quantity: 40, stackChaos: 10 });
    expect(requests.filter((r) => r.path.endsWith('/data/static'))).toHaveLength(1);
    expect(requests.filter((r) => r.path.endsWith('/exchange/Covens'))).toHaveLength(1);
    advance(180_001);
    expect(
      (await client.quoteExchangeItem(item('Betrayal Thread'), 'Covens'))?.updatedAt,
    ).toBe(301001);
    expect(requests.filter((r) => r.path.endsWith('/exchange/Covens'))).toHaveLength(2);
  });

  it('does not mix wrong pairs, offline sellers, zero stock or invalid amounts into a quote', async () => {
    const offline = offer();
    delete (offline.listing.account as { online?: unknown }).online;
    const wrongQuote = offer();
    wrongQuote.listing.offers[0]!.exchange.currency = 'divine';
    const { client } = setup({
      valid: offer(),
      wrongItem: offer('divine'),
      offline,
      empty: offer('betrayal-thread', 4, 1, 0),
      badAmount: offer('betrayal-thread', 0),
      wrongQuote,
    });
    const quote = await client.quoteExchangeItem(item('Betrayal Thread'), 'Covens');
    expect(quote?.rate?.sampleSize).toBe(1);
    expect(quote?.perItemChaos).toBe(0.25);
  });

  it('keeps an unavailable exchange rate distinct from a trade price or zero', async () => {
    const { client, requests } = setup({});
    const result = await new PriceCheckEngine(client).check(
      'Item Class: Stackable Currency\nRarity: Currency\nBetrayal Thread\n--------\nStack Size: 20/100',
      'Covens',
      null,
    );
    expect(result.exchange).toMatchObject({
      source: 'bulk-exchange',
      perItemChaos: null,
      stackChaos: null,
    });
    expect(result.typical).toBeNull();
    expect(result.total).toBe(0);
    expect(
      requests.some((r) => r.path.includes('/search/') || r.path.endsWith('/data/stats')),
    ).toBe(false);
  });

  it('routes exchangeable items through the Windows engine before item modifiers or listing filters', async () => {
    const { client, requests } = setup();
    const result = await new PriceCheckEngine(client).check(
      'Item Class: Stackable Currency\nRarity: Currency\nBetrayal Thread\n--------\nStack Size: 20/100',
      'Covens',
      null,
      'divine',
    );
    expect(result.typical).toMatchObject({ amount: 0.25, currency: 'chaos' });
    expect(result.exchange?.stackChaos).toBe(5);
    expect(result.filters).toEqual([]);
    expect(result.listings[0]).toMatchObject({ amount: 0.25, stock: 100, minimum: 4 });
    expect(
      requests.some(
        (r) =>
          r.path.includes('/search/') ||
          r.path.endsWith('/data/stats') ||
          r.path.endsWith('/data/items'),
      ),
    ).toBe(false);
  });

  it('quotes Chaos as the reference unit without an exchange request and preserves rarityless stacks', async () => {
    const { client, requests } = setup();
    expect(await client.quoteExchangeItem(item('Chaos Orb'), 'Covens')).toMatchObject({
      perItemChaos: 1,
      stackChaos: 20,
    });
    expect(requests.some((r) => r.path.includes('/exchange/'))).toBe(false);
    const parsed = parseItemText(
      'Item Class: Threads\nBetrayal Thread\n--------\nStack Size: 17/100',
    );
    if (!parsed.ok) throw new Error(parsed.message);
    expect(exchangeQuantity(parsed.item)).toBe(17);
  });

  it('preserves small fractional rates instead of displaying zero', () => {
    expect(formatExchangeAmount(0.0025)).toBe('0.0025');
    expect(formatExchangeAmount(0.025)).toBe('0.025');
    expect(formatExchangeAmount(0.0000001)).not.toBe('0');
    expect(formatExchangeAmount(5)).toBe('5');
  });

  it('samples the cheapest per-item offers rather than an overpriced tail or nominal lot prices', async () => {
    const offers = Object.fromEntries([
      ...Array.from({ length: 10 }, (_, index) => [
        `cheap-${index}`,
        offer('betrayal-thread', 100, 20, 1000),
      ]),
      ...Array.from({ length: 30 }, (_, index) => [
        `expensive-${index}`,
        offer('betrayal-thread', 1000, 10000, 10000),
      ]),
    ]);
    const { client } = setup(offers);
    const quote = await client.quoteExchangeItem(item('Betrayal Thread'), 'Covens');
    expect(quote?.rate?.sampleSize).toBe(10);
    expect(quote?.perItemChaos).toBe(0.2);
    expect(quote?.stackChaos).toBe(4);
  });
});
