/** A cluster explanation, never a searchable affix. Accept copied stat suffixes. */
export function isClusterRadiusReminder(text: string): boolean {
  const normalized = text
    .replace(/[\u200B-\u200D\uFEFF]/g, '')
    .replace(/\s+/g, ' ')
    .trim()
    .replace(/\s*\((?:enchant|implicit|explicit)\)\s*$/i, '')
    .replace(/[().]/g, '')
    .trim();
  return /^Added Passive Skills are never considered to be in Radius by other Jewels$/i.test(
    normalized,
  );
}
