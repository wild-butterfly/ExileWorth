import { describe, expect, it } from 'vitest';
import { resolveLeagueId, type League } from './league';

const leagues: League[] = [
  'Standard',
  'Hardcore Covens',
  'Covens',
  'Ruthless Covens',
].map((id) => ({ id, text: id, realm: 'pc' }));

describe('league rollover', () => {
  it('replaces an expired league with current softcore trade', () => {
    expect(resolveLeagueId(leagues, 'Allflame')).toBe('Covens');
    expect(resolveLeagueId(leagues, null)).toBe('Covens');
  });

  it('honors valid permanent and challenge selections', () => {
    expect(resolveLeagueId(leagues, 'Standard')).toBe('Standard');
    expect(resolveLeagueId(leagues, 'Hardcore Covens')).toBe('Hardcore Covens');
  });

  it('does not use a stale league before the catalog loads', () => {
    expect(resolveLeagueId([], 'Allflame')).toBeNull();
    expect(resolveLeagueId(leagues.slice(0, 1), 'Allflame')).toBe('Standard');
  });
});
