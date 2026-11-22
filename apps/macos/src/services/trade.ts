import {
  TradeClient,
  CurrencyHistoryClient,
  loadMarketCatalog,
} from '@vaaluation/trade-client';
import type { FetchLike, MarketCatalog } from '@vaaluation/trade-client';

export const APP_VERSION = '0.1.13';
export const PROJECT_URL = 'https://github.com/wild-butterfly/ExileWorth';

/**
 * GGG's developer policy requires requests to identify the application and
 * offer a contact point. Nothing user-specific is included.
 */
export const USER_AGENT = `ExileWorth/${APP_VERSION} (+${PROJECT_URL})`;

let client: TradeClient | null = null;
let historyClient: CurrencyHistoryClient | null = null;

/** A fetch that gives up after 15 seconds, so a stalled request never hangs the UI. */
const timedFetch: FetchLike = async (url, options) => {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 15_000);
  try {
    const response = await globalThis.fetch(url, {
      ...options,
      signal: controller.signal,
    });
    const body = await response.text();
    return {
      ok: response.ok,
      status: response.status,
      headers: response.headers,
      text: async () => body,
    };
  } finally {
    clearTimeout(timer);
  }
};

export function getCurrencyHistoryClient(): CurrencyHistoryClient {
  historyClient ??= new CurrencyHistoryClient({
    userAgent: USER_AGENT,
    fetch: timedFetch,
  });
  return historyClient;
}

let marketCatalog: Promise<MarketCatalog> | null = null;

/**
 * Item names (the community RePoE table, GGG-owned game data downloaded at
 * runtime, never bundled) and GGG's catalogue, loaded once per session.
 */
export function getMarketCatalog(): Promise<MarketCatalog> {
  marketCatalog ??= loadMarketCatalog(
    timedFetch,
    USER_AGENT,
    getTradeClient().getStaticData(),
  ).catch((error: unknown) => {
    marketCatalog = null;
    throw error;
  });
  return marketCatalog;
}

export function getTradeClient(): TradeClient {
  client ??= new TradeClient({
    fetch: globalThis.fetch as unknown as FetchLike,
    userAgent: USER_AGENT,
  });
  return client;
}
