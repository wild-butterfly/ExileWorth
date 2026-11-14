const console = require('node:console');

const { createInterface } = require('node:readline');

const leagues = [{ id: 'Covens', text: 'Covens' }];
const lines = createInterface({ input: process.stdin });
lines.on('line', (line) => {
  const request = JSON.parse(line);
  if (request.action === 'leagues') {
    console.log(JSON.stringify({ result: { leagues, selected: 'Covens' } }));
  } else if (request.text?.includes('Divine Orb')) {
    console.log(
      JSON.stringify({
        result: {
          leagues,
          selected: 'Covens',
          total: 3,
          itemName: 'Divine Orb',
          listings: [
            { amount: 42, currency: 'chaos', accountName: 'Fixture', presence: 'online' },
            { amount: 43, currency: 'chaos', accountName: 'FixtureTwo', presence: 'afk' },
            { amount: 44, currency: 'chaos', accountName: 'FixtureThree', presence: 'online' },
          ],
          filters: [],
          tradeUrl: 'https://www.pathofexile.com/trade/search/Covens/fixture',
        },
      }),
    );
  } else {
    console.log(JSON.stringify({ error: 'The expected copy did not reach the worker.' }));
  }
});
