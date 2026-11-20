import { TradeClient, CurrencyHistoryClient } from '@vaaluation/trade-client';
import type { FetchLike } from '@vaaluation/trade-client';

export const APP_VERSION = '0.1.7';
export const PROJECT_URL = 'https://github.com/wild-butterfly/ExileWorth';

/**
 * GGG's developer policy requires requests to identify the application and
 * offer a contact point. Nothing user-specific is included.
 */
export const USER_AGENT = `ExileWorth/${APP_VERSION} (+${PROJECT_URL})`;

let client: TradeClient | null = null;
let historyClient: CurrencyHistoryClient | null = null;

export function getCurrencyHistoryClient(): CurrencyHistoryClient {
  historyClient ??= new CurrencyHistoryClient({
    userAgent: USER_AGENT,
    fetch: async (url, options) => {
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
    },
  });
  return historyClient;
}

export function getTradeClient(): TradeClient {
  client ??= new TradeClient({
    fetch: globalThis.fetch as unknown as FetchLike,
    userAgent: USER_AGENT,
  });
  return client;
}
