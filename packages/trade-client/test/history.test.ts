import { describe, expect, it } from 'vitest';
import {
  CurrencyHistoryClient,
  historyHours,
  historyPoint,
  parseHistoryDigest,
} from '../src/history';
import { CURRENCY_METADATA } from '../src/currencyMetadata';
import { TRACKED_CURRENCIES } from '../src/exchange';

const hour = Date.UTC(2026, 10, 20, 10);
const divine = CURRENCY_METADATA.divine!,
  chaos = CURRENCY_METADATA.chaos!;
function digest(
  at: number,
  quantities = { [divine]: 10, [chaos]: 2000 },
  league = 'Covens',
) {
  return {
    next_change_id: at / 1000 + 3600,
    markets: [{ league, market_pair: [divine, chaos], volume_traded: quantities }],
  };
}

describe('official currency history', () => {
  it('maps every supported currency, not just the three preview examples', () => {
    for (const currency of TRACKED_CURRENCIES)
      expect(CURRENCY_METADATA[currency.id]).toMatch(/^Metadata\/Items\//);
    expect(new Set(Object.values(CURRENCY_METADATA)).size).toBe(
      TRACKED_CURRENCIES.length + 1,
    );
  });
  it.each(['24h', '7d', '30d'] as const)(
    'requests only completed hours for %s, in time order',
    (period) => {
      const hours = historyHours(period, hour + 1_234_567);
      expect(hours.at(-1)).toBe(hour - 3_600_000);
      expect(
        hours.every(
          (at, index) => at % 3_600_000 === 0 && (index === 0 || at > hours[index - 1]!),
        ),
      ).toBe(true);
      expect(hours.length).toBe(period === '24h' ? 24 : period === '7d' ? 8 : 31);
    },
  );
  it('uses actual traded quantities and supports reciprocal pairs', () => {
    const markets = parseHistoryDigest(digest(hour), hour);
    expect(historyPoint(markets, 'Covens', 'divine', 'chaos', hour)).toMatchObject({
      rate: 200,
      giveVolume: 10,
      wantVolume: 2000,
    });
    expect(historyPoint(markets, 'Covens', 'chaos', 'divine', hour).rate).toBe(0.005);
    expect(historyPoint(markets, 'Standard', 'divine', 'chaos', hour).rate).toBeNull();
  });
  it('keeps missing, zero-volume, malformed and unpublished hours as gaps', () => {
    expect(parseHistoryDigest(digest(hour, { [divine]: 0, [chaos]: 1 }), hour)).toEqual(
      [],
    );
    expect(
      parseHistoryDigest(
        { next_change_id: hour / 1000, markets: digest(hour).markets },
        hour,
      ),
    ).toEqual([]);
    expect(
      parseHistoryDigest(
        {
          next_change_id: hour / 1000 + 3600,
          markets: [null, { league: 'Covens', volume_traded: {} }],
        },
        hour,
      ),
    ).toEqual([]);
    expect(historyPoint([], 'Covens', 'divine', 'chaos', hour).rate).toBeNull();
  });
  it('reuses completed digests across pair changes and stops after a failed request', async () => {
    const calls: string[] = [];
    const client = new CurrencyHistoryClient({
      now: () => hour + 3_600_000,
      userAgent: 'test',
      fetch: async (url) => {
        calls.push(url);
        const requested = Number(url.split('/').at(-1)) * 1000;
        return {
          ok: true,
          status: 200,
          headers: { get: () => null },
          text: async () => JSON.stringify(digest(requested)),
        };
      },
    });
    const forward = await client.history('Covens', 'divine', 'chaos', '24h');
    const reverse = await client.history('Covens', 'chaos', 'divine', '24h');
    expect(calls).toHaveLength(24);
    expect(forward.points.at(-1)?.rate).toBe(200);
    expect(reverse.points.at(-1)?.rate).toBe(0.005);
    const failed = new CurrencyHistoryClient({
      userAgent: 'test',
      fetch: async () => {
        throw new Error('429');
      },
    });
    expect(await failed.history('Covens', 'divine', 'chaos', '30d')).toEqual({
      points: [],
      failedHours: 1,
    });
    expect(await failed.history('Covens', 'divine', 'chaos', '30d', () => true)).toEqual({
      points: [],
      failedHours: 0,
    });
  });
});
