/** A cluster explanation, never a searchable affix. Accept copied stat suffixes. */
function normalizeReminder(text: string): string {
  return text
    .replace(/[\u200B-\u200D\uFEFF]/g, '')
    .replace(/\s+/g, ' ')
    .trim()
    .replace(/\s*\((?:enchant|implicit|explicit)\)\s*$/i, '')
    .replace(/[().]/g, '')
    .trim();
}

export function isClusterRadiusReminder(text: string): boolean {
  const normalized = normalizeReminder(text);
  return /^Added Passive Skills are never considered to be in Radius by other Jewels$/i.test(
    normalized,
  );
}

/** Only known explanatory text; unknown modifiers must still be reported. */
export function isModifierReminder(text: string): boolean {
  return (
    isClusterRadiusReminder(text) ||
    /^(Fire|Cold|Lightning) Exposure applies -\d+% to \1 Resistance for \d+ seconds$/i.test(
      normalizeReminder(text),
    )
  );
}
