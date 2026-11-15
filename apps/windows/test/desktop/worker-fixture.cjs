const console = require('node:console');

const { createInterface } = require('node:readline');
const { setTimeout } = require('node:timers');

const leagues = [{ id: 'Covens', text: 'Covens' }];
const lines = createInterface({ input: process.stdin });
let checks = 0;
// Later lookups answer late, so the test can dismiss the overlay mid-search.
const reply = (body) => setTimeout(() => console.log(body), checks > 1 ? 2500 : 0);
lines.on('line', (line) => {
  const request = JSON.parse(line);
  if (request.action === 'check') checks += 1;
  if (request.action === 'leagues') {
    console.log(JSON.stringify({ result: { leagues, selected: 'Covens' } }));
  } else if (request.text?.includes('Divine Orb')) {
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
          filters: [],
          tradeUrl: 'https://www.pathofexile.com/trade/search/Covens/fixture',
        },
      }),
    );
  } else {
    reply(JSON.stringify({ error: 'The expected copy did not reach the worker.' }));
  }
});
