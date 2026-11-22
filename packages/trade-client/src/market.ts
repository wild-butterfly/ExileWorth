import type { FetchLike } from './client';
import type { CurrencyHistoryClient, Market } from './history';
import { absoluteIconUrl } from './icons';

/**
 * The currency market: every item traded on GGG's in-game Currency Exchange,
 * priced from completed trades.
 *
 * GGG publishes an hourly digest of every exchange market (both sides' traded
 * volumes, per league), keyed by internal item ids such as
 * "Metadata/Items/Currency/CurrencyRerollRare". Names for those ids come from
 * the community RePoE data (game-file data owned by GGG), fetched at runtime
 * and never bundled; categories and icons come from GGG's own trade catalogue.
 */

export const CHAOS_ID = 'Metadata/Items/Currency/CurrencyRerollRare';
export const DIVINE_ID = 'Metadata/Items/Currency/CurrencyModValues';

/** Game-file item names (RePoE, maintained fork). Owned by GGG; loaded on demand. */
export const REPOE_BASE_ITEMS_URL = 'https://repoe-fork.github.io/base_items.min.json';

export type MarketPeriod = '24h' | '7d' | '30d';

/** Categories in reading order. Keys are GGG's own trade catalogue group ids. */
export const MARKET_CATEGORIES: readonly { id: string; label: string }[] = [
  { id: 'Currency', label: 'Currency' },
  { id: 'Fragments', label: 'Fragments' },
  { id: 'Essences', label: 'Essences' },
  { id: 'Delve', label: 'Fossils & Resonators' },
  { id: 'Oils', label: 'Oils' },
  { id: 'Catalysts', label: 'Catalysts' },
  { id: 'DeliriumOrbs', label: 'Delirium Orbs' },
  { id: 'Ancestor', label: 'Tattoos & Omens' },
  { id: 'Runegrafts', label: 'Runegrafts' },
  { id: 'Keepers', label: 'Foulborn & Wombgifts' },
  { id: 'Expedition', label: 'Expedition' },
  { id: 'EnshroudingCrystals', label: 'Crystals' },
  { id: 'Sanctum', label: 'Sanctum' },
  { id: 'Heist', label: 'Heist' },
  { id: 'Beasts', label: 'Beasts' },
  { id: 'Cards', label: 'Divination Cards' },
  { id: 'MapKey', label: 'Maps' },
  { id: 'Other', label: 'Other' },
];

export interface MarketItem {
  /** GGG's internal item id, as used by the exchange digests. */
  readonly id: string;
  readonly name: string;
  /** One of MARKET_CATEGORIES' ids. */
  readonly category: string;
  /** Absolute icon URL on GGG's CDN, when the trade catalogue has one. */
  readonly icon: string | null;
  /** The trade site's exchange id ("chaos", "divine", …), when known. */
  readonly tradeId: string | null;
}

function record(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

const normal = (text: string) =>
  text.normalize('NFKC').trim().replace(/\s+/g, ' ').toLowerCase();

/** "CurrencyEssenceWoe6" → "Currency Essence Woe 6": an honest label for an unknown id. */
function fallbackName(id: string): string {
  const leaf = id.split('/').at(-1) ?? id;
  return leaf
    .replace(/([a-z])([A-Z0-9])/g, '$1 $2')
    .replace(/([0-9])([A-Za-z])/g, '$1 $2')
    .trim();
}

/** Names and categories for exchange item ids. */
export class MarketCatalog {
  private readonly names = new Map<string, { name: string; itemClass: string }>();
  private readonly byName = new Map<
    string,
    { category: string; icon: string | null; tradeId: string }
  >();
  private readonly items = new Map<string, MarketItem>();

  /**
   * @param baseItems RePoE `base_items` JSON: metadata id → { name, item_class }.
   * @param staticData GGG `/api/trade/data/static` response.
   */
  constructor(baseItems: unknown, staticData: unknown) {
    if (!record(baseItems)) throw new Error('The item name table could not be read.');
    for (const [id, entry] of Object.entries(baseItems)) {
      if (!record(entry) || typeof entry.name !== 'string' || entry.name.trim() === '')
        continue;
      this.names.set(id, {
        name: entry.name.trim(),
        itemClass: typeof entry.item_class === 'string' ? entry.item_class : '',
      });
    }
    const known = new Set(MARKET_CATEGORIES.map((category) => category.id));
    if (record(staticData) && Array.isArray(staticData.result)) {
      for (const group of staticData.result) {
        if (
          !record(group) ||
          typeof group.id !== 'string' ||
          !Array.isArray(group.entries)
        )
          continue;
        const category = known.has(group.id) ? group.id : 'Other';
        for (const entry of group.entries) {
          if (
            !record(entry) ||
            typeof entry.text !== 'string' ||
            typeof entry.id !== 'string'
          )
            continue;
          const key = normal(entry.text);
          if (this.byName.has(key)) continue;
          this.byName.set(key, {
            category,
            icon:
              typeof entry.image === 'string' && entry.image !== ''
                ? absoluteIconUrl(entry.image)
                : null,
            tradeId: entry.id,
          });
        }
      }
    }
  }

  item(id: string): MarketItem {
    const cached = this.items.get(id);
    if (cached) return cached;
    const named = this.names.get(id);
    const name = named?.name ?? fallbackName(id);
    const official = this.byName.get(normal(name));
    const category =
      official?.category ??
      (named?.itemClass === 'DivinationCard' || id.includes('/DivinationCards/')
        ? 'Cards'
        : id.includes('/MapFragments/')
          ? 'Fragments'
          : 'Other');
    const item: MarketItem = {
      id,
      name,
      category,
      icon: official?.icon ?? null,
      tradeId: official?.tradeId ?? null,
    };
    this.items.set(id, item);
    return item;
  }
}

/** Fetches the RePoE name table and GGG's catalogue, and builds the catalog. */
export async function loadMarketCatalog(
  fetch: FetchLike,
  userAgent: string,
  staticData: Promise<unknown>,
): Promise<MarketCatalog> {
  const response = await fetch(REPOE_BASE_ITEMS_URL, {
    headers: { 'User-Agent': userAgent, Accept: 'application/json' },
  });
  if (!response.ok) throw new Error(`Item names unavailable (${response.status}).`);
  return new MarketCatalog(JSON.parse(await response.text()), await staticData);
}

/** Traded volumes for one item against Chaos and against Divine over some hours. */
interface Totals {
  chaos: number;
  itemForChaos: number;
  divine: number;
  itemForDivine: number;
}

/**
 * Volume-weighted Chaos prices for every item over a set of hourly digests.
 * Trades against Divine Orbs count too, converted at the same hours' Divine
 * price, so an item traded mostly for Divines is still priced. Trades between
 * two other items cannot be valued and are left out.
 */
export function priceHours(
  digests: readonly (readonly Market[])[],
  league: string,
): Map<string, { chaos: number; volume: number }> {
  const totals = new Map<string, Totals>();
  const entry = (id: string) => {
    let found = totals.get(id);
    if (!found)
      totals.set(
        id,
        (found = { chaos: 0, itemForChaos: 0, divine: 0, itemForDivine: 0 }),
      );
    return found;
  };
  for (const markets of digests) {
    for (const market of markets) {
      if (market.league !== league) continue;
      const ids = Object.keys(market.volume);
      if (ids.length !== 2) continue;
      const chaos = market.volume[CHAOS_ID];
      const divine = market.volume[DIVINE_ID];
      const other = ids.find((id) => id !== CHAOS_ID && id !== DIVINE_ID);
      if (chaos !== undefined && divine !== undefined) {
        const totalsForDivine = entry(DIVINE_ID);
        totalsForDivine.chaos += chaos;
        totalsForDivine.itemForChaos += divine;
      } else if (other !== undefined && chaos !== undefined) {
        const totalsForItem = entry(other);
        totalsForItem.chaos += chaos;
        totalsForItem.itemForChaos += market.volume[other]!;
      } else if (other !== undefined && divine !== undefined) {
        const totalsForItem = entry(other);
        totalsForItem.divine += divine;
        totalsForItem.itemForDivine += market.volume[other]!;
      }
    }
  }
  const divineTotals = totals.get(DIVINE_ID);
  const divineChaos =
    divineTotals && divineTotals.itemForChaos > 0
      ? divineTotals.chaos / divineTotals.itemForChaos
      : null;
  const prices = new Map<string, { chaos: number; volume: number }>();
  prices.set(CHAOS_ID, { chaos: 1, volume: 0 });
  for (const [id, item] of totals) {
    const valuedDivine = divineChaos !== null && id !== DIVINE_ID;
    const chaos = item.chaos + (valuedDivine ? item.divine * divineChaos! : 0);
    const count = item.itemForChaos + (valuedDivine ? item.itemForDivine : 0);
    if (count <= 0 || chaos <= 0) continue;
    const price = chaos / count;
    if (Number.isFinite(price) && price > 0)
      prices.set(id, { chaos: price, volume: item.itemForChaos + item.itemForDivine });
  }
  return prices;
}

const HOUR = 3_600_000;
const DAY = 24 * HOUR;

/**
 * The hours each point of a period's series aggregates, oldest first.
 * 24h: each hour. 7d: four hours per day (every six hours). 30d: one hour a day.
 */
export function periodBuckets(period: MarketPeriod, latestHour: number): number[][] {
  if (period === '24h') {
    return Array.from({ length: 24 }, (_, index) => [latestHour - (23 - index) * HOUR]);
  }
  if (period === '7d') {
    return Array.from({ length: 7 }, (_, index) => {
      const end = latestHour - (6 - index) * DAY;
      return [0, 6, 12, 18].map((offset) => end - offset * HOUR).reverse();
    });
  }
  return Array.from({ length: 30 }, (_, index) => [latestHour - (29 - index) * DAY]);
}

export interface MarketRow {
  readonly item: MarketItem;
  /** Chaos value of one item over the last 24 hours of completed trades. */
  readonly chaos: number;
  /** Items traded over those 24 hours. */
  readonly volume: number;
  /** Chaos value per point of the period, oldest first; null where nothing traded. */
  readonly series: readonly (number | null)[];
  /** Percent change between the first and last priced points, if both exist. */
  readonly change: number | null;
}

export interface MarketSnapshot {
  readonly league: string;
  readonly period: MarketPeriod;
  /** Start of the newest completed hour included. */
  readonly latestHour: number;
  readonly rows: readonly MarketRow[];
  /** Hours that could not be loaded; their points stay gaps. */
  readonly failedHours: number;
}

export function percentChange(series: readonly (number | null)[]): number | null {
  const priced = series.filter((value): value is number => value !== null);
  if (priced.length < 2) return null;
  const first = priced[0]!;
  const last = priced.at(-1)!;
  return first > 0 ? ((last - first) / first) * 100 : null;
}

async function loadHours(
  history: CurrencyHistoryClient,
  hours: readonly number[],
  cancelled: () => boolean,
): Promise<{ digests: Map<number, Market[]>; failed: number }> {
  const digests = new Map<number, Market[]>();
  let failed = 0;
  for (const hour of hours) {
    if (cancelled()) break;
    try {
      digests.set(hour, await history.markets(hour));
    } catch {
      failed += 1;
      // Stop after repeated failures rather than hammering the feed.
      if (failed >= 3) break;
    }
  }
  return { digests, failed };
}

/**
 * Prices every traded item for a league. The price and volume always cover
 * the last 24 hours; the series covers the chosen period.
 */
export async function loadMarket(
  history: CurrencyHistoryClient,
  catalog: MarketCatalog,
  league: string,
  period: MarketPeriod,
  cancelled: () => boolean = () => false,
): Promise<MarketSnapshot> {
  const latestHour = history.latestHour();
  const window = periodBuckets('24h', latestHour);
  const buckets = period === '24h' ? window : periodBuckets(period, latestHour);
  const hours = [...new Set([...window.flat(), ...buckets.flat()])].sort((a, b) => b - a);
  const { digests, failed } = await loadHours(history, hours, cancelled);
  const hoursOf = (bucket: readonly number[]) =>
    bucket.flatMap((hour) => {
      const markets = digests.get(hour);
      return markets ? [markets] : [];
    });

  const current = priceHours(hoursOf(window.flat()), league);
  const points = buckets.map((bucket) => priceHours(hoursOf(bucket), league));
  const rows: MarketRow[] = [];
  for (const [id, price] of current) {
    if (id === CHAOS_ID) continue;
    const series = points.map((point) => point.get(id)?.chaos ?? null);
    rows.push({
      item: catalog.item(id),
      chaos: price.chaos,
      volume: price.volume,
      series,
      change: percentChange(series),
    });
  }
  rows.sort((a, b) => b.chaos - a.chaos);
  return { league, period, latestHour, rows, failedHours: failed };
}

export interface ItemHistoryPoint {
  readonly at: number;
  /** Value of one item in the chosen unit; null where either side did not trade. */
  readonly value: number | null;
  readonly volume: number | null;
}

/** One item's value in a unit (any traded item) across a period. */
export async function loadItemHistory(
  history: CurrencyHistoryClient,
  league: string,
  itemId: string,
  unitId: string,
  period: MarketPeriod,
  cancelled: () => boolean = () => false,
): Promise<{ points: ItemHistoryPoint[]; failedHours: number }> {
  const buckets = periodBuckets(period, history.latestHour());
  const { digests, failed } = await loadHours(
    history,
    [...new Set(buckets.flat())].sort((a, b) => b - a),
    cancelled,
  );
  const points = buckets.map((bucket) => {
    const prices = priceHours(
      bucket.flatMap((hour) => {
        const markets = digests.get(hour);
        return markets ? [markets] : [];
      }),
      league,
    );
    const item = prices.get(itemId);
    const unit = prices.get(unitId);
    return {
      at: bucket.at(-1)!,
      value: item && unit ? item.chaos / unit.chaos : null,
      volume: item?.volume ?? null,
    };
  });
  return { points, failedHours: failed };
}

/**
 * Converts a Chaos value into a unit. "auto" reads like a player would say
 * it: Divines once the value is worth at least one, Chaos below that.
 */
export function inUnit(
  chaos: number,
  unit: string,
  unitChaos: ReadonlyMap<string, number>,
): { amount: number; unit: string } | null {
  if (unit === 'auto') {
    const divine = unitChaos.get(DIVINE_ID);
    return divine !== undefined && chaos >= divine
      ? { amount: chaos / divine, unit: DIVINE_ID }
      : { amount: chaos, unit: CHAOS_ID };
  }
  const rate = unit === CHAOS_ID ? 1 : unitChaos.get(unit);
  return rate !== undefined && rate > 0 ? { amount: chaos / rate, unit } : null;
}
