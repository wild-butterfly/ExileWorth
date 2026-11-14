import { TradeClient } from '@vaaluation/trade-client';
import type { FetchLike } from '@vaaluation/trade-client';

export const APP_VERSION = '0.1.3';
export const PROJECT_URL = 'https://github.com/wild-butterfly/ExileWorth';

/**
 * GGG's developer policy requires requests to identify the application and
 * offer a contact point. Nothing user-specific is included.
 */
export const USER_AGENT = `ExileWorth/${APP_VERSION} (+${PROJECT_URL})`;

let client: TradeClient | null = null;

export function getTradeClient(): TradeClient {
  client ??= new TradeClient({
    fetch: globalThis.fetch as unknown as FetchLike,
    userAgent: USER_AGENT,
  });
  return client;
}
