export interface League {
  /** League id as used by the trade API, e.g. "Standard". */
  readonly id: string;
  /** Display name shown to the user. */
  readonly text: string;
  readonly realm: 'pc';
}

const permanentLeagues = new Set([
  'Standard',
  'Hardcore',
  'Ruthless',
  'Hardcore Ruthless',
]);

/** Keep an available choice; replace an expired choice using the current PC catalog. */
export function resolveLeagueId(
  leagues: readonly League[],
  preferred: string | null,
): string | null {
  if (preferred !== null && leagues.some(({ id }) => id === preferred)) return preferred;

  const challenges = leagues.filter(({ id }) => !permanentLeagues.has(id));
  const softcore = challenges.find(
    ({ id }) => !/^(?:HC|Hardcore|Ruthless|SSF)\b/i.test(id),
  );
  return softcore?.id ?? challenges[0]?.id ?? leagues[0]?.id ?? null;
}
