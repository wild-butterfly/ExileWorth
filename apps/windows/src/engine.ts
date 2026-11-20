import { parseItemText } from '@vaaluation/item-parser';
import { resolveLeagueId } from '@vaaluation/shared-types';
import {
  buildQuery,
  distribution,
  relaxWeakest,
  toPricedListings,
  tradeSearchUrl,
  type TradeClient,
} from '@vaaluation/trade-client';
import { windowsFilters, type WindowsFilter } from './filters';

/** Currencies the listing filter may restrict prices to. */
const LISTING_CURRENCIES = new Set(['alch', 'chaos', 'divine', 'exalted']);

export class PriceCheckEngine {
  private item: ReturnType<typeof parseItemText> | null = null;
  private filters: WindowsFilter[] = [];

  constructor(private readonly client: TradeClient) {}

  async leagues(preferred: string | null) {
    const leagues = await this.client.getLeagues();
    return { leagues, selected: resolveLeagueId(leagues, preferred) };
  }

  async check(
    text: string | null,
    preferred: string | null,
    enabled: readonly string[] | null,
    currency?: string,
    minimums?: Readonly<Record<string, number>>,
  ) {
    if (text !== null) {
      this.item = parseItemText(text);
      this.filters = [];
    }
    if (!this.item?.ok) throw new Error(this.item?.message ?? 'Copy an item first.');
    const { item } = this.item;
    const { leagues, selected } = await this.leagues(preferred);
    if (selected === null) throw new Error('No PC trade leagues are available.');
    const stats = await this.client.getStatIndex();
    const baseTypes = await this.client.getBaseTypeIndex();
    if (text !== null || this.filters.length === 0)
      this.filters = windowsFilters(item, stats);
    const requested = enabled === null ? null : new Set(enabled);
    let filters =
      requested === null
        ? this.filters
        : this.filters.map((filter) => ({
            ...filter,
            selected: requested.has(filter.key),
          }));
    filters = filters.map((filter) => {
      const min = minimums?.[filter.key];
      return typeof min === 'number' && Number.isFinite(min) && filter.value !== null
        ? { ...filter, min, max: null }
        : filter;
    });
    const combined = filters.find((filter) => filter.selected && filter.combinedKeys);
    if (combined)
      filters = filters.map((filter) =>
        combined.combinedKeys!.includes(filter.key)
          ? { ...filter, selected: false }
          : filter,
      );
    const searchListings = () => {
      const query = buildQuery(item, filters, { baseTypes });
      if (currency !== undefined && LISTING_CURRENCIES.has(currency)) {
        return this.client.search(selected, {
          ...query,
          query: {
            ...query.query,
            filters: {
              ...query.query.filters,
              trade_filters: { filters: { price: { option: currency } } },
            },
          },
        });
      }
      return this.client.search(selected, query);
    };
    let search = await searchListings();
    // Relax only automatic searches; honor a manually chosen filter set exactly.
    for (
      let attempt = 0;
      enabled === null && search.total === 0 && attempt < 2;
      attempt++
    ) {
      const next = relaxWeakest(filters);
      if (next === null) break;
      filters = next;
      search = await searchListings();
    }
    this.filters = filters;
    const fetched = await this.client.fetchListings(search.result, search.id);
    const byId = new Map(fetched.map((entry) => [entry.id, entry]));
    const listings = toPricedListings(
      search.result.slice(0, 10).flatMap((id) => {
        const entry = byId.get(id);
        return entry === undefined ? [] : [entry];
      }),
    );
    return {
      leagues,
      selected,
      total: search.total,
      listings,
      typical: await this.typical(selected, listings),
      itemName: item.name,
      filters: filters.map(
        ({ key, label, selected, value, min, combinedKeys, tooltip }) => ({
          key,
          label,
          enabled: selected,
          value,
          min,
          combinedKeys,
          tooltip,
        }),
      ),
      tradeUrl: tradeSearchUrl(selected, search.id),
    };
  }

  /**
   * The headline price: the median asking price, restated in Divine Orbs when
   * it is worth at least one. The exchange rate is cached by the client, so
   * repeated checks do not each spend a request; if it fails, the chaos
   * figure stands on its own.
   */
  private async typical(
    league: string,
    listings: ReturnType<typeof toPricedListings>,
  ): Promise<{ amount: number; currency: string; chaos: number | null } | null> {
    const spread = distribution(listings);
    if (spread === null) return null;
    if (spread.currency !== 'chaos') {
      return { amount: spread.median, currency: spread.currency, chaos: null };
    }
    try {
      const rates = await this.client.currencyRates(league, ['divine'], 'chaos');
      const divine = rates.get('divine')?.median ?? 0;
      if (divine > 0 && spread.median >= divine) {
        return {
          amount: spread.median / divine,
          currency: 'divine',
          chaos: spread.median,
        };
      }
    } catch {
      // A convenience, not the answer.
    }
    return { amount: spread.median, currency: 'chaos', chaos: null };
  }
}
