import { describe, expect, it } from 'vitest';
import {
  CHAOS_ID,
  DIVINE_ID,
  MarketCatalog,
  inUnit,
  loadItemHistory,
  loadMarket,
  percentChange,
  periodBuckets,
  priceHours,
} from '../src/market';
import type { CurrencyHistoryClient, Market } from '../src/history';

const ESSENCE = 'Metadata/Items/Currency/CurrencyEssenceWoe6';
const CARD = 'Metadata/Items/DivinationCards/DivinationCardThunderousSkies';
const EXALTED = 'Metadata/Items/Currency/CurrencyAddModToRare';
const market = (league: string, volume: Record<string, number>): Market => ({
  league,
  volume,
});

// Mocked: shapes follow GGG's /api/trade/data/static and RePoE base_items.
const catalog = new MarketCatalog(
  {
    [CHAOS_ID]: { name: 'Chaos Orb', item_class: 'StackableCurrency' },
    [DIVINE_ID]: { name: 'Divine Orb', item_class: 'StackableCurrency' },
    [ESSENCE]: { name: 'Shrieking Essence of Woe', item_class: 'StackableCurrency' },
    [CARD]: { name: 'Thunderous Skies', item_class: 'DivinationCard' },
  },
  {
    result: [
      {
        id: 'Currency',
        entries: [{ id: 'divine', text: 'Divine Orb', image: '/gen/image/divine.png' }],
      },
      {
        id: 'Essences',
        entries: [{ id: 'shrieking-essence-of-woe', text: 'Shrieking Essence of Woe' }],
      },
    ],
  },
);

describe('market pricing from completed exchange trades', () => {
  it('weights Chaos trades by volume and converts Divine-side trades', () => {
    const prices = priceHours(
      [
        [
          market('Covens', { [CHAOS_ID]: 2000, [DIVINE_ID]: 10 }), // 200c per Divine
          market('Covens', { [ESSENCE]: 10, [CHAOS_ID]: 30 }),
          market('Covens', { [ESSENCE]: 20, [DIVINE_ID]: 1 }), // 20 for 200c
          market('Standard', { [ESSENCE]: 1, [CHAOS_ID]: 999 }), // another league
          market('Covens', { [ESSENCE]: 5, [EXALTED]: 1 }), // cannot be valued
        ],
      ],
      'Covens',
    );
    expect(prices.get(DIVINE_ID)).toEqual({ chaos: 200, volume: 10 });
    // (30c + 1 Divine × 200c) / 30 essences
    expect(prices.get(ESSENCE)!.chaos).toBeCloseTo(230 / 30);
    expect(prices.get(ESSENCE)!.volume).toBe(30);
    expect(prices.has(EXALTED)).toBe(false);
  });

  it('leaves Divine-side trades out entirely when there is no Divine price', () => {
    const prices = priceHours(
      [
        [
          market('Covens', { [ESSENCE]: 10, [CHAOS_ID]: 30 }),
          market('Covens', { [ESSENCE]: 90, [DIVINE_ID]: 1 }),
        ],
      ],
      'Covens',
    );
    expect(prices.get(ESSENCE)!.chaos).toBe(3);
  });

  it('samples 24 hours, four hours a day for 7 days, and one hour a day for 30 days', () => {
    const latest = Date.UTC(2026, 10, 22, 15);
    const day = periodBuckets('24h', latest);
    expect(day).toHaveLength(24);
    expect(day.at(-1)).toEqual([latest]);
    const week = periodBuckets('7d', latest);
    expect(week).toHaveLength(7);
    expect(week.every((bucket) => bucket.length === 4)).toBe(true);
    expect(week.at(-1)!.at(-1)).toBe(latest);
    expect(periodBuckets('30d', latest)).toHaveLength(30);
  });

  it('names items from game data and files them under GGG categories', () => {
    expect(catalog.item(ESSENCE)).toMatchObject({
      name: 'Shrieking Essence of Woe',
      category: 'Essences',
    });
    expect(catalog.item(DIVINE_ID)).toMatchObject({
      category: 'Currency',
      tradeId: 'divine',
      icon: 'https://web.poecdn.com/gen/image/divine.png',
    });
    expect(catalog.item(CARD).category).toBe('Cards');
    expect(catalog.item('Metadata/Items/Currency/CurrencyNewLeagueThing2')).toMatchObject(
      {
        name: 'Currency New League Thing 2',
        category: 'Other',
      },
    );
  });

  it('shows values in any unit, or Divines once worth one in auto', () => {
    const rates = new Map([
      [DIVINE_ID, 200],
      [ESSENCE, 4],
    ]);
    expect(inUnit(500, 'auto', rates)).toEqual({ amount: 2.5, unit: DIVINE_ID });
    expect(inUnit(50, 'auto', rates)).toEqual({ amount: 50, unit: CHAOS_ID });
    expect(inUnit(50, ESSENCE, rates)).toEqual({ amount: 12.5, unit: ESSENCE });
    expect(inUnit(50, EXALTED, rates)).toBeNull();
    expect(percentChange([null, 10, null, 15])).toBe(50);
    expect(percentChange([10])).toBeNull();
  });

  it('loads a snapshot and an item history through the digest client', async () => {
    const latest = Date.UTC(2026, 10, 22, 15);
    const requested: number[] = [];
    const history = {
      latestHour: () => latest,
      markets: async (hour: number) => {
        requested.push(hour);
        const rising = hour === latest ? 2 : 1;
        return [
          market('Covens', { [CHAOS_ID]: 200, [DIVINE_ID]: 1 }),
          market('Covens', { [ESSENCE]: 10, [CHAOS_ID]: 30 * rising }),
        ];
      },
    } as unknown as CurrencyHistoryClient;
    const snapshot = await loadMarket(history, catalog, 'Covens', '24h');
    expect(new Set(requested).size).toBe(24);
    expect(snapshot.rows.map((row) => row.item.name)).toEqual([
      'Divine Orb',
      'Shrieking Essence of Woe',
    ]);
    const essence = snapshot.rows[1]!;
    expect(essence.series).toHaveLength(24);
    expect(essence.series.at(-1)).toBe(6);
    expect(essence.change).toBe(100);
    const week = await loadItemHistory(history, 'Covens', ESSENCE, DIVINE_ID, '7d');
    expect(week.points).toHaveLength(7);
    expect(week.points.at(-1)!.value).toBeGreaterThan(week.points[0]!.value!);
  });
});
