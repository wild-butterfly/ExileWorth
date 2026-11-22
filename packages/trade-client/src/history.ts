import type { FetchLike } from './client';
import { CURRENCY_METADATA } from './currencyMetadata';

export type HistoryPeriod = '24h' | '7d' | '30d';
export interface HistoryPoint {
  readonly at: number;
  readonly rate: number | null;
  readonly giveVolume: number | null;
  readonly wantVolume: number | null;
}
export interface CurrencyHistory {
  readonly points: HistoryPoint[];
  readonly failedHours: number;
}
export interface Market {
  readonly league: string;
  readonly volume: Readonly<Record<string, number>>;
}

const HOUR = 3_600_000;
/** Longer views sample completed hourly digests daily, plus the latest completed hour. */
export function historyHours(period: HistoryPeriod, now: number): number[] {
  const end = Math.floor(now / HOUR) * HOUR - HOUR;
  const count = period === '24h' ? 24 : period === '7d' ? 168 : 720;
  const start = end - (count - 1) * HOUR;
  const step = period === '24h' ? HOUR : 24 * HOUR;
  const hours: number[] = [];
  for (let at = start; at <= end; at += step) hours.push(at);
  if (hours.at(-1) !== end) hours.push(end);
  return hours;
}

function record(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** Preserve actual traded quantities; a missing or zero-volume hour is a gap, not a zero price. */
export function parseHistoryDigest(body: unknown, hour: number): Market[] {
  if (
    !record(body) ||
    body.next_change_id !== hour / 1000 + 3600 ||
    !Array.isArray(body.markets)
  )
    return [];
  return body.markets.flatMap((market) => {
    if (
      !record(market) ||
      typeof market.league !== 'string' ||
      !record(market.volume_traded) ||
      !Array.isArray(market.market_pair) ||
      market.market_pair.length !== 2
    )
      return [];
    const pair = market.market_pair;
    if (!pair.every((id) => typeof id === 'string') || pair[0] === pair[1]) return [];
    const volume: Record<string, number> = {};
    for (const id of pair as string[]) {
      const amount = market.volume_traded[id];
      if (typeof amount !== 'number' || !Number.isFinite(amount) || amount <= 0)
        return [];
      volume[id] = amount;
    }
    return [{ league: market.league, volume }];
  });
}

export function historyPoint(
  markets: readonly Market[],
  league: string,
  give: string,
  want: string,
  at: number,
): HistoryPoint {
  if (give === want) return { at, rate: 1, giveVolume: null, wantVolume: null };
  const giveId = CURRENCY_METADATA[give],
    wantId = CURRENCY_METADATA[want];
  if (!giveId || !wantId) return { at, rate: null, giveVolume: null, wantVolume: null };
  const market = markets.find(
    (entry) =>
      entry.league === league &&
      entry.volume[giveId] !== undefined &&
      entry.volume[wantId] !== undefined,
  );
  const giveVolume = market?.volume[giveId] ?? null,
    wantVolume = market?.volume[wantId] ?? null;
  const rate =
    giveVolume !== null && wantVolume !== null ? wantVolume / giveVolume : null;
  return {
    at,
    rate: rate !== null && Number.isFinite(rate) && rate > 0 ? rate : null,
    giveVolume,
    wantVolume,
  };
}

/** Public, account-free GGG history. Completed digests are shared across pairs and bounded in memory. */
export class CurrencyHistoryClient {
  private readonly cache = new Map<number, Market[]>();
  private readonly pending = new Map<number, Promise<Market[]>>();
  constructor(
    private readonly options: { fetch: FetchLike; userAgent: string; now?: () => number },
  ) {}

  private digest(hour: number): Promise<Market[]> {
    const cached = this.cache.get(hour);
    if (cached) return Promise.resolve(cached);
    const pending = this.pending.get(hour);
    if (pending) return pending;
    const request = (async () => {
      const response = await this.options.fetch(
        `https://web.poecdn.com/api/currency-exchange/${hour / 1000}`,
        { headers: { 'User-Agent': this.options.userAgent, Accept: 'application/json' } },
      );
      if (!response.ok)
        throw new Error(`Exchange history unavailable (${response.status}).`);
      const body: unknown = JSON.parse(await response.text());
      if (
        !record(body) ||
        !Array.isArray(body.markets) ||
        typeof body.next_change_id !== 'number'
      )
        throw new Error('Invalid exchange history response.');
      const markets = parseHistoryDigest(body, hour);
      // An unpublished latest hour can be retried later; never cache it as permanent history.
      if (body.next_change_id === hour / 1000 + 3600) {
        this.cache.set(hour, markets);
        // 24 hourly digests plus the 7-day samples fit with room to spare.
        if (this.cache.size > 160) this.cache.delete(this.cache.keys().next().value!);
      }
      return markets;
    })();
    this.pending.set(hour, request);
    void request.finally(() => this.pending.delete(hour)).catch(() => {});
    return request;
  }

  /** Every market traded in one completed hour, across all leagues. */
  markets(hour: number): Promise<Market[]> {
    return this.digest(hour);
  }

  /** The latest completed hour, the newest digest GGG publishes. */
  latestHour(): number {
    return Math.floor((this.options.now ?? Date.now)() / HOUR) * HOUR - HOUR;
  }

  async history(
    league: string,
    give: string,
    want: string,
    period: HistoryPeriod,
    cancelled: () => boolean = () => false,
  ): Promise<CurrencyHistory> {
    const points: HistoryPoint[] = [];
    let failedHours = 0;
    for (const at of historyHours(period, (this.options.now ?? Date.now)()).reverse()) {
      if (cancelled()) break;
      try {
        points.push(
          historyPoint(
            give === want ? [] : await this.digest(at),
            league,
            give,
            want,
            at,
          ),
        );
      } catch {
        // Stop on a network or rate-limit failure instead of hammering the remaining hours.
        failedHours++;
        break;
      }
    }
    return { points: points.sort((a, b) => a.at - b.at), failedHours };
  }
}
