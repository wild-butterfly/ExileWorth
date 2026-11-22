import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { isModifierReminder, parseItemText, separateReminders } from '../src';

const fixture = (name: string) => readFileSync(join(__dirname, 'fixtures', name), 'utf8');

describe('copied explanatory text', () => {
  it.each([
    '(All Added Passive Skills are Small unless otherwise specified)',
    '(Added Passive Skills are never considered to be in Radius by other Jewels)',
    '(All Added Passive Skills are Small unless otherwise specified) (enchant)',
    '(Added Passive Skills are never considered to be in Radius by other Jewels.)',
    '(Added Passive Skills are never considered to be in Radius by other Jewels). (enchant)',
    '(All Added Passive Skills are Small unless otherwise specified) (Added Passive Skills are never considered to be in Radius by other Jewels)',
    '(All Added Passive Skills are Small unless otherwise specified)'.replaceAll(
      ' ',
      '\u00a0',
    ),
    '(All Added Passive\u200b Skills are Small unless otherwise specified)',
    // An explanation the game may add later: recognised by form, not by wording.
    '(Some future explanation of how this modifier behaves)',
    'All Added Passive Skills are Small unless otherwise specified',
    'Fire Exposure applies -10% to Fire Resistance for 4 seconds',
  ])('treats %j as explanatory text', (line) => {
    expect(isModifierReminder(line)).toBe(true);
  });

  it.each([
    'Adds 8 Passive Skills',
    'Adds 8 Passive Skills (enchant)',
    '2 Added Passive Skills are Jewel Sockets (enchant)',
    'Added Small Passive Skills also grant: +3% to all Elemental Resistances',
    '1 Added Passive Skill is Life from Death',
    '(enchant)',
    '(Local)',
    '(12)',
    '(An unknown explanation that never closes',
    'Quality (Caster Modifiers): +12%',
  ])('keeps %j as a modifier', (line) => {
    expect(isModifierReminder(line)).toBe(false);
  });

  it('joins an explanation split over lines, but never swallows an unterminated one', () => {
    expect(
      separateReminders([
        'Adds 3 Passive Skills (enchant)',
        '(All Added Passive Skills are Small unless',
        'otherwise specified) (enchant)',
        'Added Small Passive Skills grant: +15% to Cold Resistance (enchant)',
      ]),
    ).toEqual({
      modifiers: [
        'Adds 3 Passive Skills (enchant)',
        'Added Small Passive Skills grant: +15% to Cold Resistance (enchant)',
      ],
      reminders: [
        '(All Added Passive Skills are Small unless otherwise specified) (enchant)',
      ],
    });
    const unterminated = ['(An explanation that never closes', 'Adds 8 Passive Skills'];
    expect(separateReminders(unterminated)).toEqual({
      modifiers: unterminated,
      reminders: [],
    });
  });

  it.each([
    ['cluster-jewel-large-advanced.txt', 2],
    ['cluster-jewel-large-plain-multiline.txt', 2],
    ['cluster-jewel-medium-advanced.txt', 1],
    ['cluster-jewel-small-both-reminders.txt', 2],
  ])('keeps every explanation out of the modifiers of %s', (name, reminderLines) => {
    const result = parseItemText(fixture(name));
    if (!result.ok || result.item.kind !== 'equipment')
      throw new Error('Expected equipment');
    const { modifiers, unknownLines } = result.item;
    expect(modifiers.some((modifier) => isModifierReminder(modifier.text))).toBe(false);
    expect(modifiers.some((modifier) => modifier.text.includes('('))).toBe(false);
    expect(unknownLines.filter((line) => isModifierReminder(line))).toHaveLength(
      reminderLines,
    );
  });

  it('joins a passive description copied on the line after its heading', () => {
    const result = parseItemText(fixture('cluster-jewel-large-plain-multiline.txt'));
    if (!result.ok || result.item.kind !== 'equipment')
      throw new Error('Expected equipment');
    expect(result.item.modifiers.map(({ text, type }) => [text, type])).toEqual([
      ['Adds 8 Passive Skills', 'enchant'],
      ['2 Added Passive Skills are Jewel Sockets', 'enchant'],
      [
        'Added Small Passive Skills grant: Wand Attacks deal 12% increased Damage with Hits and Ailments',
        'enchant',
      ],
      [
        'Added Small Passive Skills also grant: +3% to all Elemental Resistances',
        'explicit',
      ],
    ]);
  });
});
