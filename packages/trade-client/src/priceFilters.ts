import type { ParsedItem } from '@vaaluation/shared-types';
import { buildFilters, type SelectableFilter } from './query';
import type { StatIndex } from './stats';

export function applyMinimumPreset(
  filters: readonly SelectableFilter[],
  percent: number,
): SelectableFilter[] {
  if (![0, 5, 10, 20].includes(percent)) throw new Error('Unknown minimum preset');
  return filters.map((filter) =>
    filter.selected && filter.value !== null && !filter.exact
      ? {
          ...filter,
          min:
            Math.round((filter.value - (Math.abs(filter.value) * percent) / 100) * 100) /
            100,
          max: null,
        }
      : filter,
  );
}

export function matchingMinimumPreset(
  filters: readonly SelectableFilter[],
): number | null {
  const selected = filters.filter(
    (filter) => filter.selected && filter.value !== null && !filter.exact,
  );
  if (!selected.length) return null;
  return (
    [0, 5, 10, 20].find((percent) =>
      selected.every(
        (filter) =>
          filter.min !== null &&
          Math.abs(
            filter.min -
              Math.round(
                (filter.value! - (Math.abs(filter.value!) * percent) / 100) * 100,
              ) /
                100,
          ) < 0.000001,
      ),
    ) ?? null
  );
}

export function resistanceMode(
  filters: readonly SelectableFilter[],
  combined: boolean,
): SelectableFilter[] {
  const group = filters.find((filter) => filter.combinedKeys?.length);
  if (!group) return [...filters];
  const enabled = combined
    ? filters.some(
        (filter) => group.combinedKeys!.includes(filter.key) && filter.selected,
      )
    : group.selected;
  return filters.map((filter) =>
    filter.key === group.key
      ? { ...filter, selected: combined && enabled }
      : group.combinedKeys!.includes(filter.key)
        ? { ...filter, selected: !combined && enabled }
        : filter,
  );
}

export function visiblePriceFilters(
  filters: readonly SelectableFilter[],
  combined: boolean,
): SelectableFilter[] {
  const group = filters.find((filter) => filter.combinedKeys?.length);
  return filters.filter(
    (filter) =>
      !group ||
      (combined ? !group.combinedKeys!.includes(filter.key) : filter.key !== group.key),
  );
}

/** Offer a combined row only when every contributing elemental roll is understood. */
export function buildPriceFilters(
  item: ParsedItem,
  stats: StatIndex,
): SelectableFilter[] {
  const filters: SelectableFilter[] = buildFilters(item, stats).map((filter) => ({
    ...filter,
    min: filter.min ?? filter.value,
  }));
  if (item.kind !== 'equipment') return filters;
  const totals = { Fire: 0, Cold: 0, Lightning: 0 };
  const keys: string[] = [];
  for (const [index, modifier] of item.modifiers.entries()) {
    if (!/(?:Fire|Cold|Lightning|Elemental).*Resistanc/i.test(modifier.text)) continue;
    const match = modifier.text.match(
      /^([+-]?\d+(?:\.\d+)?)% to (all Elemental|Fire|Cold|Lightning|Fire and Cold|Fire and Lightning|Cold and Lightning) Resistances?$/i,
    );
    // Conditional or unfamiliar resistance modifiers must not silently inflate the total.
    if (!match) return filters;
    const group = match[2]!.toLowerCase();
    for (const element of Object.keys(totals) as (keyof typeof totals)[]) {
      if (group === 'all elemental' || group.includes(element.toLowerCase()))
        totals[element] += Number(match[1]);
    }
    const source = filters.find((filter) => filter.key.startsWith(`${index}:`));
    if (!source) return filters;
    keys.push(source.key);
  }
  const total = totals.Fire + totals.Cold + totals.Lightning;
  if (total <= 0 || Object.values(totals).filter((value) => value !== 0).length < 2)
    return filters;
  const match = stats.match({
    text: `+${total}% total Elemental Resistance`,
    type: 'explicit',
  });
  if (!match || !match.id.startsWith('pseudo.')) return filters;
  const first = filters.findIndex((filter) => keys.includes(filter.key));
  filters.splice(first, 0, {
    key: 'combined:elemental-resistance',
    kind: 'stat',
    statId: match.id,
    label: `Total elemental res.: ${total}%`,
    value: total,
    min: total,
    max: null,
    selected: false,
    weight: 50,
    combinedKeys: keys,
    tooltip: `${totals.Fire}% fire + ${totals.Cold}% cold + ${totals.Lightning}% lightning = ${total}% total elemental resistance. Searches accept any combination meeting the total; individual resistance minimums are replaced.`,
  });
  return filters;
}
