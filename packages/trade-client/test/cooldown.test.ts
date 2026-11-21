import { describe, expect, it } from 'vitest';
import { RateLimitPolicy } from '../src/rateLimit';
import { TradeClient } from '../src/client';

const headers = (values: Record<string, string>) => ({
  get: (key: string) => values[key] ?? null,
});
describe('cooldown expiry', () => {
  it('does not restart an old restriction when another response has no state headers', () => {
    let now = 1_000;
    const policy = new RateLimitPolicy(() => now);
    policy.update(
      headers({ 'x-rate-limit-ip': '5:10:300', 'x-rate-limit-ip-state': '5:10:300' }),
    );
    now += 200_000;
    policy.update(headers({}));
    expect(policy.retryAfterMs()).toBe(100_000);
    now += 100_001;
    policy.update(headers({}));
    expect(policy.retryAfterMs()).toBe(0);
  });
  it('uses the longest exhausted window and does not reset it on headerless responses', () => {
    let now = 1_000;
    const policy = new RateLimitPolicy(() => now);
    policy.update(
      headers({
        'x-rate-limit-ip': '5:10:60,15:60:300',
        'x-rate-limit-ip-state': '5:10:0,15:60:0',
      }),
    );
    expect(policy.retryAfterMs()).toBe(60_000);
    now += 30_000;
    policy.update(headers({}));
    expect(policy.retryAfterMs()).toBe(30_000);
    now += 30_001;
    expect(policy.retryAfterMs()).toBe(0);
  });

  it('does not let a short explicit restriction hide a longer exhausted window', () => {
    const policy = new RateLimitPolicy(() => 1000);
    policy.update(
      headers({
        'x-rate-limit-ip': '5:10:60,15:60:300',
        'x-rate-limit-ip-state': '5:10:10,15:60:0',
      }),
    );
    expect(policy.retryAfterMs()).toBe(60_000);
  });
  it('pauses all trade endpoints after a 429 and can recover after the deadline', async () => {
    let now = 1_000,
      calls = 0;
    const client = new TradeClient({
      now: () => now,
      userAgent: 'test',
      fetch: async () => {
        calls++;
        return calls === 1
          ? {
              ok: false,
              status: 429,
              headers: headers({ 'retry-after': '300' }),
              text: async () => '{}',
            }
          : {
              ok: true,
              status: 200,
              headers: headers({}),
              text: async () => '{"result":[{"id":"Covens","realm":"pc"}]}',
            };
      },
    });
    const query = {
      query: { status: { option: 'online' as const }, stats: [] },
      sort: { price: 'asc' as const },
    };
    await expect(client.search('Covens', query)).rejects.toMatchObject({
      code: 'rate_limited',
      retryAfterMs: 300_000,
    });
    now += 100_000;
    await expect(client.getLeagues()).rejects.toMatchObject({
      code: 'rate_limited',
      retryAfterMs: 200_000,
    });
    expect(calls).toBe(1);
    now += 200_001;
    expect(await client.getLeagues()).toEqual([
      { id: 'Covens', text: 'Covens', realm: 'pc' },
    ]);
    expect(calls).toBe(2);
  });
});
