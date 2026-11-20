import { describe, expect, it } from 'vitest';
import {
  applyMinimumPreset,
  matchingMinimumPreset,
  resistanceMode,
  visiblePriceFilters,
} from '../src/priceFilters';
import type { SelectableFilter } from '../src/query';

const row = (key: string, value: number, selected = true): SelectableFilter => ({
  key,
  label: key,
  value,
  min: value,
  max: null,
  kind: 'stat',
  statId: key,
  weight: 1,
  selected,
});
describe('shared editable price filters', () => {
  it('presets use the original roll, only affect checked filters, and reset exactly', () => {
    const original = [
      row('life', 80),
      row('speed', 1.25),
      row('off', 100, false),
      row('penalty', -20),
    ];
    const first = applyMinimumPreset(original, 10);
    expect(first.map((filter) => filter.min)).toEqual([72, 1.13, 100, -22]);
    expect(applyMinimumPreset(first, 20).map((filter) => filter.min)).toEqual([
      64, 1, 100, -24,
    ]);
    expect(applyMinimumPreset(first, 0)).toEqual(original);
    expect(matchingMinimumPreset(first)).toBe(10);
    expect(matchingMinimumPreset([{ ...first[0]!, min: 71 }])).toBeNull();
  });
  it('switches between combined and individual filters without duplicate search requirements', () => {
    const original = [
      row('life', 80),
      row('fire', 40),
      row('cold', 30),
      { ...row('total', 70, false), combinedKeys: ['fire', 'cold'] },
    ];
    const combined = resistanceMode(original, true);
    expect(
      combined.filter((filter) => filter.selected).map((filter) => filter.key),
    ).toEqual(['life', 'total']);
    expect(visiblePriceFilters(combined, true).map((filter) => filter.key)).toEqual([
      'life',
      'total',
    ]);
    const individual = resistanceMode(combined, false);
    expect(
      individual.filter((filter) => filter.selected).map((filter) => filter.key),
    ).toEqual(['life', 'fire', 'cold']);
    expect(visiblePriceFilters(individual, false).map((filter) => filter.key)).toEqual([
      'life',
      'fire',
      'cold',
    ]);
  });
});
