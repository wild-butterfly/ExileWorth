import type { ParsedItem } from '@vaaluation/shared-types';
import type { CurrencyRate, ExchangeOffer } from './exchange';
import { TradeError } from './types';

export interface ExchangeItem {
  readonly id: string;
  readonly name: string;
}

export interface ExchangeQuote {
  readonly item: ExchangeItem;
  readonly source: 'bulk-exchange';
  readonly perItemChaos: number | null;
  readonly quantity: number;
  readonly stackChaos: number | null;
  readonly rate: CurrencyRate | null;
  readonly offers: readonly ExchangeOffer[];
  readonly updatedAt: number;
  readonly url: string;
}

const normalized = (text: string) =>
  text.normalize('NFKC').trim().replace(/\s+/g, ' ').toLowerCase();

/** Uses every named entry in GGG's exchange catalogue, not a currency whitelist. */
export class ExchangeItemIndex {
  private readonly names = new Map<string, ExchangeItem | null>();

  constructor(body: unknown) {
    const data = body as {
      result?: { entries?: { id?: unknown; text?: unknown }[] }[];
    } | null;
    if (!Array.isArray(data?.result))
      throw new TradeError(
        'unexpected_response',
        'The exchange catalogue could not be read. Try again later.',
      );
    for (const group of data.result) {
      if (!Array.isArray(group?.entries)) continue;
      for (const entry of group.entries) {
        if (
          typeof entry?.id !== 'string' ||
          typeof entry.text !== 'string' ||
          entry.id === 'sep' ||
          !entry.id ||
          !entry.text.trim()
        )
          continue;
        const key = normalized(entry.text);
        const previous = this.names.get(key);
        if (previous === undefined)
          this.names.set(key, { id: entry.id, name: entry.text });
        else if (previous?.id !== entry.id) this.names.set(key, null);
      }
    }
  }

  resolve(item: ParsedItem): ExchangeItem | null {
    // Rolled gear keeps its modifier-aware item search even if a name happens
    // to collide with a commodity. Gems likewise need level/quality matching.
    if (item.kind === 'gem' || (item.kind === 'equipment' && item.rarity !== 'normal'))
      return null;
    if (
      item.kind === 'map' &&
      (item.rarity !== 'normal' || item.corrupted || (item.quality ?? 0) > 0)
    )
      return null;
    return this.names.get(normalized(item.name)) ?? null;
  }
}

export function exchangeQuantity(item: ParsedItem): number {
  const value = item.stackSize?.current ?? 1;
  return Number.isSafeInteger(value) && value > 0 ? value : 1;
}

/** Preserve sub-chaos rates; a cheap exchange item must never display as zero. */
export function formatExchangeAmount(value: number): string {
  return Number.isFinite(value) && value >= 0
    ? Number(value.toPrecision(4)).toString()
    : '—';
}
