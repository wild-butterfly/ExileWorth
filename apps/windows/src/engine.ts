import { parseItemText } from '@vaaluation/item-parser';
import { resolveLeagueId } from '@vaaluation/shared-types';
import {
  buildFilters,
  buildQuery,
  relaxWeakest,
  toPricedListings,
  tradeSearchUrl,
  type SelectableFilter,
  type TradeClient,
} from '@vaaluation/trade-client';

export class PriceCheckEngine {
  private item: ReturnType<typeof parseItemText> | null = null;
  private filters: SelectableFilter[] = [];

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
      this.filters = buildFilters(item, stats);
    const requested = enabled === null ? null : new Set(enabled);
    let filters =
      requested === null
        ? this.filters
        : this.filters.map((filter) => ({
            ...filter,
            selected: requested.has(filter.key),
          }));
    const searchListings = () => {
      const query = buildQuery(item, filters, { baseTypes });
      if (currency === 'alch' || currency === 'chaos' || currency === 'divine') {
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
      itemName: item.name,
      filters: filters.map(({ key, label, selected }) => ({
        key,
        label,
        enabled: selected,
      })),
      tradeUrl: tradeSearchUrl(selected, search.id),
    };
  }
}
