/**
 * Telling copied explanatory ("reminder") text apart from real modifiers.
 *
 * Path of Exile copies reminder text on its own line, wrapped in parentheses:
 *
 *   Adds 8 Passive Skills (enchant)
 *   (All Added Passive Skills are Small unless otherwise specified)
 *   (Added Passive Skills are never considered to be in Radius by other Jewels)
 *
 * A real modifier is never written that way, so the rule is structural rather
 * than a list of sentences: earlier fixes matched one explanation at a time and
 * every new one surfaced as an "unsupported modifier". Anything that is not
 * wholly parenthesised prose stays a modifier, so a genuinely unsupported
 * modifier is still reported rather than silently dropped.
 */

/** Copy suffixes and type tags the game (or advanced copy) may append. */
const TYPE_SUFFIX =
  /\s*\((?:enchant|implicit|explicit|crafted|fractured|rune|scourge|augmented)\)\s*$/i;

/** Invisible characters and non-breaking spaces seen in copied text. */
function clean(text: string): string {
  return text
    .replace(/[\u200B-\u200D\u2060\uFEFF]/g, '')
    .replace(/[\u00A0\u202F]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

/** Removes trailing type tags and full stops, which may follow a reminder. */
function stripTrailers(text: string): string {
  let current = clean(text);
  for (;;) {
    const next = current.replace(TYPE_SUFFIX, '').replace(/\s*\.\s*$/, '');
    if (next === current) return current;
    current = next;
  }
}

/** Prose inside parentheses: words, not a lone number or type tag. */
const PAREN_GROUP = /\(([^()]*[A-Za-z][^()]*)\)/y;

/**
 * Whether the whole line is one or more parenthesised explanations, e.g.
 * "(A) (B)", "(A.)", "(A). (enchant)". A type tag alone is not a reminder.
 */
function isParenthesisedProse(line: string): boolean {
  const text = stripTrailers(line);
  if (text === '' || !text.startsWith('(')) return false;
  let index = 0;
  let groups = 0;
  while (index < text.length) {
    PAREN_GROUP.lastIndex = index;
    const match = PAREN_GROUP.exec(text);
    if (match === null) return false;
    const inner = match[1]!.trim();
    // "(enchant)" on its own is a suffix, never an explanation.
    if (TYPE_SUFFIX.test(`(${inner})`) || !/\s/.test(inner)) return false;
    groups += 1;
    index = PAREN_GROUP.lastIndex;
    // Allow ". " or " " between consecutive explanations.
    while (index < text.length && /[\s.]/.test(text[index]!)) index += 1;
  }
  return groups > 0;
}

/**
 * Explanations that have been seen copied without their parentheses. Kept as
 * a fallback for the structural rule, not as the primary test.
 */
const KNOWN_BARE_REMINDERS: readonly RegExp[] = [
  /^Added Passive Skills are never considered to be in Radius by other Jewels$/i,
  /^All Added Passive Skills are Small unless otherwise specified$/i,
  /^(Fire|Cold|Lightning) Exposure applies -\d+% to \1 Resistance for \d+ seconds$/i,
];

function isKnownBareReminder(line: string): boolean {
  const text = stripTrailers(line)
    .replace(/^\(|\)$/g, '')
    .trim();
  return KNOWN_BARE_REMINDERS.some((pattern) => pattern.test(text));
}

/** Whether a single copied line is explanatory text rather than a modifier. */
export function isModifierReminder(text: string): boolean {
  return isParenthesisedProse(text) || isKnownBareReminder(text);
}

/** Kept for existing callers; every cluster explanation is now covered. */
export function isClusterRadiusReminder(text: string): boolean {
  return /never considered to be in Radius/i.test(text) && isModifierReminder(text);
}

/** Longest explanation, in lines, that may be split across copied lines. */
const MAX_REMINDER_LINES = 4;

/**
 * Splits a section's lines into modifier lines and reminder lines. A reminder
 * may also be split over several lines ("(Added Passive Skills are never" /
 * "considered to be in Radius by other Jewels)"); the run is only taken when
 * its closing parenthesis is found, so an unterminated "(" never swallows
 * real modifiers.
 */
export function separateReminders(lines: readonly string[]): {
  modifiers: string[];
  reminders: string[];
} {
  const modifiers: string[] = [];
  const reminders: string[] = [];
  for (let index = 0; index < lines.length; index++) {
    const line = lines[index]!;
    if (isModifierReminder(line)) {
      reminders.push(clean(line));
      continue;
    }
    const opened = stripTrailers(line);
    if (opened.startsWith('(') && !opened.includes(')')) {
      let joined = opened;
      let end = -1;
      for (
        let next = index + 1;
        next < lines.length && next < index + MAX_REMINDER_LINES;
        next++
      ) {
        joined = `${joined} ${clean(lines[next]!)}`;
        if (stripTrailers(lines[next]!).endsWith(')')) {
          end = next;
          break;
        }
      }
      if (end !== -1 && isModifierReminder(joined)) {
        reminders.push(clean(joined));
        index = end;
        continue;
      }
    }
    modifiers.push(line);
  }
  return { modifiers, reminders };
}
