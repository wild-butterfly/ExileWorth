const console = require('node:console');

const { readFileSync, writeFileSync } = require('node:fs');
const { join } = require('node:path');
const { createInterface } = require('node:readline');
const { setTimeout } = require('node:timers');

const leagues = [{ id: 'Covens', text: 'Covens' }];
const lines = createInterface({ input: process.stdin });
let checks = 0;
let copiedText = null;
// Later lookups answer late, so the test can dismiss the overlay mid-search.
const reply = (body) => setTimeout(() => console.log(body), checks === 2 ? 2500 : 0);
lines.on('line', (line) => {
  const request = JSON.parse(line);
  if (request.action === 'icons') {
    console.log(JSON.stringify({ result: {} }));
    return;
  }
  if (request.action === 'check' && process.env.EXILEWORTH_TEST_EXCHANGE === '1') {
    console.log(
      JSON.stringify({
        result: JSON.parse(
          readFileSync(join(__dirname, 'exchange-snapshot.json'), 'utf8'),
        ),
      }),
    );
    return;
  }
  if (request.action === 'check' && request.text !== null && request.text !== undefined) {
    checks += 1;
    copiedText = request.text;
  }
  if (
    request.action === 'check' &&
    request.minimums?.['fixture:life'] === 72 &&
    request.enabled?.includes('fixture:life')
  ) {
    writeFileSync(process.env.EXILEWORTH_TEST_FILTERS, 'verified');
  }
  if (request.action === 'leagues') {
    console.log(JSON.stringify({ result: { leagues, selected: 'Covens' } }));
  } else if (request.action === 'trades') {
    const expected = [
      'Hi, one moment please.',
      'Sorry, that one is already sold.',
      'Hi, are you still interested?',
    ];
    if (
      request.lines.some((line) => line.includes('@From FixtureBuyer:')) &&
      JSON.stringify(request.replies) === JSON.stringify(expected)
    ) {
      writeFileSync(process.env.EXILEWORTH_TEST_REPLIES, 'verified');
    }
    console.log(
      JSON.stringify({
        result: [
          {
            character: 'FixtureBuyer',
            summary: 'Goldrim — 1 chaos',
            direction: 'incoming',
            reply: '@FixtureBuyer Thanks!',
            invite: '/invite FixtureBuyer',
            trade: '/tradewith FixtureBuyer',
            kick: '/kick FixtureBuyer',
            replies: request.replies.map((message) => `@FixtureBuyer ${message}`),
          },
        ],
      }),
    );
  } else if (copiedText?.includes('Divine Orb')) {
    reply(
      JSON.stringify({
        result: {
          leagues,
          selected: 'Covens',
          total: 3,
          itemName: 'Divine Orb',
          listings: [
            { amount: 42, currency: 'chaos', accountName: 'Fixture', presence: 'online' },
            { amount: 43, currency: 'chaos', accountName: 'FixtureTwo', presence: 'afk' },
            {
              amount: 44,
              currency: 'chaos',
              accountName: 'FixtureThree',
              presence: 'online',
            },
          ],
          filters: [
            {
              key: 'fixture:life',
              label: '+80 to maximum Life',
              enabled: true,
              value: 80,
              min: request.minimums?.['fixture:life'] ?? 80,
            },
          ],
          tradeUrl: 'https://www.pathofexile.com/trade/search/Covens/fixture',
        },
      }),
    );
  } else {
    reply(JSON.stringify({ error: 'The expected copy did not reach the worker.' }));
  }
});
