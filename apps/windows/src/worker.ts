import { createInterface } from 'node:readline';
import {
  currencyBatches,
  TRACKED_CURRENCIES,
  TradeClient,
  CurrencyHistoryClient,
  TradeError,
  CHAOS_ID,
  loadItemHistory,
  loadMarket,
  loadMarketCatalog,
  type MarketCatalog,
  type MarketPeriod,
  type HistoryPeriod,
  type CurrencyRate,
} from '@vaaluation/trade-client';
import {
  describeRequest,
  inviteCommand,
  kickCommand,
  parseTradeWhisper,
  thanksCommand,
  tradeCommand,
  whisperCommand,
} from '@vaaluation/trade-whispers';
import { PriceCheckEngine } from './engine';

// Keep requests bounded so a stalled connection does not freeze the UI.
const client = new TradeClient({
  fetch: (url, options) =>
    fetch(url, { ...options, signal: AbortSignal.timeout(15_000) }),
  userAgent: 'ExileWorth/0.1.25 (+https://github.com/wild-butterfly/ExileWorth)',
});
const engine = new PriceCheckEngine(client);
const historyClient = new CurrencyHistoryClient({
  userAgent: 'ExileWorth/0.1.25 (+https://github.com/wild-butterfly/ExileWorth)',
  fetch: (url, options) =>
    fetch(url, { ...options, signal: AbortSignal.timeout(15_000) }),
});
// Item names (RePoE, GGG-owned game data) and GGG's catalogue load once per
// worker, only when the Currency page asks for them.
let catalog: Promise<MarketCatalog> | null = null;
function marketCatalog(): Promise<MarketCatalog> {
  catalog ??= loadMarketCatalog(
    (url, options) => fetch(url, { ...options, signal: AbortSignal.timeout(20_000) }),
    'ExileWorth/0.1.25 (+https://github.com/wild-butterfly/ExileWorth)',
    client.getStaticData(),
  ).catch((error: unknown) => {
    catalog = null;
    throw error;
  });
  return catalog;
}
const isPeriod = (value: unknown): value is MarketPeriod =>
  value === '24h' || value === '7d' || value === '30d';
/** GGG item ids look like Metadata/Items/…; nothing else is accepted. */
const isItemId = (value: unknown): value is string =>
  typeof value === 'string' && /^Metadata\/[A-Za-z0-9/_]+$/.test(value);

const historyCurrencies = [
  { id: 'chaos', label: 'Chaos Orb' },
  ...TRACKED_CURRENCIES.map(({ id, label }) => ({ id, label })),
];

async function currency(preferred: string | null) {
  const { selected } = await engine.leagues(preferred);
  if (!selected) throw new Error('No PC trade league is available.');
  const rows: { id: string; rate: CurrencyRate | null; error: string | null }[] = [];
  for (const batch of currencyBatches()) {
    try {
      const rates = await client.currencyRates(selected, batch, 'chaos', {
        waitForCapacity: true,
      });
      for (const id of batch) rows.push({ id, rate: rates.get(id) ?? null, error: null });
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Exchange request failed.';
      for (const id of batch) rows.push({ id, rate: null, error: message });
    }
  }
  return {
    selected,
    rows: TRACKED_CURRENCIES.filter(({ id }) => id !== 'chaos').map(({ id, label }) => ({
      label,
      ...(rows.find((row) => row.id === id) ?? { id, rate: null, error: null }),
    })),
  };
}

const lines = createInterface({ input: process.stdin, crlfDelay: Infinity });

async function run() {
  for await (const line of lines) {
    try {
      const request = JSON.parse(line) as {
        action: string;
        preferred: string | null;
        text?: string;
        enabled?: string[];
        give?: string;
        want?: string;
        period?: HistoryPeriod;
        minimums?: Record<string, number>;
        currency?: string;
        lines?: string[];
        thanks?: string;
        replies?: string[];
        item?: unknown;
        unit?: unknown;
      };
      if (request.action === 'market') {
        const { selected } = await engine.leagues(request.preferred);
        if (!selected) throw new Error('No PC trade league is available.');
        const period = request.period ?? '24h';
        if (!isPeriod(period)) throw new Error('Unsupported market period.');
        const items = await marketCatalog();
        const snapshot = await loadMarket(historyClient, items, selected, period);
        process.stdout.write(
          JSON.stringify({
            result: {
              league: snapshot.league,
              period: snapshot.period,
              latestHour: snapshot.latestHour,
              failedHours: snapshot.failedHours,
              chaosIcon: items.item(CHAOS_ID).icon,
              rows: snapshot.rows.map((row) => ({
                id: row.item.id,
                name: row.item.name,
                category: row.item.category,
                icon: row.item.icon,
                chaos: row.chaos,
                volume: row.volume,
                series: row.series,
                change: row.change,
              })),
            },
          }) + '\n',
        );
        continue;
      }
      if (request.action === 'itemHistory') {
        const { selected } = await engine.leagues(request.preferred);
        if (!selected) throw new Error('No PC trade league is available.');
        const period = request.period ?? '7d';
        if (!isPeriod(period)) throw new Error('Unsupported history period.');
        if (!isItemId(request.item) || !isItemId(request.unit))
          throw new Error('Unsupported item.');
        process.stdout.write(
          JSON.stringify({
            result: await loadItemHistory(
              historyClient,
              selected,
              request.item,
              request.unit,
              period,
            ),
          }) + '\n',
        );
        continue;
      }
      if (request.action === 'historyCurrencies') {
        process.stdout.write(JSON.stringify({ result: historyCurrencies }) + '\n');
        continue;
      }
      if (request.action === 'history') {
        const { selected } = await engine.leagues(request.preferred);
        if (!selected) throw new Error('No PC trade league is available.');
        const give = request.give ?? 'divine',
          want = request.want ?? 'chaos';
        if (
          ![give, want].every((id) =>
            historyCurrencies.some((currency) => currency.id === id),
          )
        )
          throw new Error('Unsupported currency.');
        const period = request.period ?? '7d';
        if (!['24h', '7d', '30d'].includes(period))
          throw new Error('Unsupported history period.');
        process.stdout.write(
          JSON.stringify({
            result: await historyClient.history(selected, give, want, period),
          }) + '\n',
        );
        continue;
      }
      if (request.action === 'icons') {
        const icons = await client.getCurrencyIcons();
        process.stdout.write(
          JSON.stringify({ result: Object.fromEntries(icons) }) + '\n',
        );
        continue;
      }
      if (request.action === 'trades') {
        const requestThanks = request.thanks?.trim() || null;
        const requestReplies = (request.replies ?? [])
          .map((reply) => reply.trim())
          .filter((reply) => reply.length > 0)
          .slice(0, 8);
        const result = (request.lines ?? [])
          .flatMap((line) => {
            const request = parseTradeWhisper(line);
            if (!request) return [];
            return [
              {
                character: request.character,
                direction: request.direction,
                summary: describeRequest(request),
                receivedAt: request.receivedAt,
                raw: request.raw,
                reply: thanksCommand(request, requestThanks ?? 'Thanks!').text,
                invite: inviteCommand(request).text,
                trade: tradeCommand(request).text,
                kick: kickCommand(request).text,
                replies: requestReplies.map(
                  (reply) => whisperCommand(request, reply).text,
                ),
              },
            ];
          })
          .slice(-30)
          .reverse();
        process.stdout.write(JSON.stringify({ result }) + '\n');
        continue;
      }
      if (request.action === 'currency') {
        process.stdout.write(
          JSON.stringify({ result: await currency(request.preferred) }) + '\n',
        );
        continue;
      }
      const result =
        request.action === 'leagues'
          ? await engine.leagues(request.preferred)
          : await engine.check(
              request.text ?? null,
              request.preferred,
              request.enabled ?? null,
              request.currency,
              request.minimums,
            );
      process.stdout.write(JSON.stringify({ result }) + '\n');
    } catch (error) {
      process.stdout.write(
        JSON.stringify({
          error: error instanceof Error ? error.message : 'Search failed.',
          ...(error instanceof TradeError
            ? { code: error.code, retryAfterMs: error.retryAfterMs }
            : {}),
        }) + '\n',
      );
    }
  }
}

void run();
