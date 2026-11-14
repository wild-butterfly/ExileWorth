import { createInterface } from 'node:readline';
import { TradeClient } from '@vaaluation/trade-client';
import { PriceCheckEngine } from './engine';

const engine = new PriceCheckEngine(
  // Node's native fetch allows setting the identifying User-Agent.
  // Keep requests bounded so a stalled connection does not freeze the UI.
  new TradeClient({
    fetch: (url, options) =>
      fetch(url, { ...options, signal: AbortSignal.timeout(15_000) }),
    userAgent: 'ExileWorth/0.1.3 (+https://github.com/wild-butterfly/ExileWorth)',
  }),
);
const lines = createInterface({ input: process.stdin, crlfDelay: Infinity });

async function run() {
  for await (const line of lines) {
    try {
      const request = JSON.parse(line) as {
        action: string;
        preferred: string | null;
        text?: string;
        enabled?: string[];
      };
      const result =
        request.action === 'leagues'
          ? await engine.leagues(request.preferred)
          : await engine.check(
              request.text ?? null,
              request.preferred,
              request.enabled ?? null,
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
