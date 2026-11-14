import { createInterface } from 'node:readline';
import {
  currencyBatches,
  TRACKED_CURRENCIES,
  TradeClient,
  type CurrencyRate,
} from '@vaaluation/trade-client';
import {
  describeRequest,
  parseTradeWhisper,
  thanksCommand,
} from '@vaaluation/trade-whispers';
import { PriceCheckEngine } from './engine';

// Keep requests bounded so a stalled connection does not freeze the UI.
const client = new TradeClient({
  fetch: (url, options) =>
    fetch(url, { ...options, signal: AbortSignal.timeout(15_000) }),
  userAgent: 'ExileWorth/0.1.10 (+https://github.com/wild-butterfly/ExileWorth)',
});
const engine = new PriceCheckEngine(client);

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
      ...rows.find((row) => row.id === id),
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
        currency?: string;
        lines?: string[];
      };
      if (request.action === 'trades') {
        const result = (request.lines ?? [])
          .flatMap((line) => {
            const request = parseTradeWhisper(line);
            if (!request) return [];
            return [
              {
                character: request.character,
                direction: request.direction,
                summary: describeRequest(request),
                reply: thanksCommand(request, 'Thanks!').text,
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
            );
      process.stdout.write(JSON.stringify({ result }) + '\n');
    } catch (error) {
      process.stdout.write(
        JSON.stringify({
          error: error instanceof Error ? error.message : 'Search failed.',
        }) + '\n',
      );
    }
  }
}

void run();
