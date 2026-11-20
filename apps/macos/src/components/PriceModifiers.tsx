import React, { useEffect, useMemo, useRef, useState } from 'react';
import { Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import {
  applyMinimumPreset,
  matchingMinimumPreset,
  resistanceMode,
  visiblePriceFilters,
  type SelectableFilter,
} from '@vaaluation/trade-client';
import {
  borders,
  fonts,
  radii,
  spacing,
  surfaces,
  text as palette,
  useTheme,
} from '@vaaluation/ui';

interface Props {
  filters: SelectableFilter[];
  onChange: (filters: SelectableFilter[]) => void;
  combined: boolean;
  disabled?: boolean;
}

function MinimumInput({
  filter,
  disabled,
  onChange,
  onValidity,
}: {
  filter: SelectableFilter;
  disabled: boolean;
  onChange: (value: number) => void;
  onValidity: (valid: boolean) => void;
}) {
  const value = filter.selected ? filter.min : (filter.min ?? filter.value);
  const [raw, setRaw] = useState(value === null ? '' : String(value));
  const [valid, setValid] = useState(true);
  const rawRef = useRef(raw);
  const inputRef = useRef<TextInput>(null);
  useEffect(() => {
    if (rawRef.current.trim() !== '' && Number(rawRef.current) === value) return;
    const next = value === null ? '' : String(value);
    rawRef.current = next;
    setRaw(next);
    setValid(true);
    onValidity(true);
  }, [value, onValidity]);
  return (
    <TextInput
      ref={inputRef}
      style={[styles.input, !valid && styles.invalid]}
      accessibilityLabel={`${filter.label} search minimum`}
      value={raw}
      editable={!disabled}
      selectTextOnFocus
      onChangeText={(next) => {
        rawRef.current = next;
        setRaw(next);
        const ok = next.trim() !== '' && Number.isFinite(Number(next));
        setValid(ok);
        onValidity(ok);
        if (ok) onChange(Number(next));
      }}
      onSubmitEditing={() => inputRef.current?.blur()}
      onKeyPress={(event) => {
        if (event.nativeEvent.key === 'Escape' && filter.value !== null)
          onChange(filter.value);
      }}
      placeholder="any"
      placeholderTextColor={palette.faint}
    />
  );
}

export function PriceModifiers({
  filters,
  onChange,
  combined,
  disabled = false,
  onValidityChange,
}: Props & { onValidityChange: (valid: boolean) => void }) {
  const theme = useTheme();
  const invalid = useRef(new Set<string>());
  const signature = filters.map((filter) => filter.key).join('|');
  useEffect(() => {
    invalid.current.clear();
    onValidityChange(true);
  }, [signature, onValidityChange]);
  const callbacks = useMemo(
    () =>
      new Map(
        signature
          .split('|')
          .filter(Boolean)
          .map((key) => [
            key,
            (valid: boolean) => {
              if (valid) invalid.current.delete(key);
              else invalid.current.add(key);
              onValidityChange(invalid.current.size === 0);
            },
          ]),
      ),
    [signature, onValidityChange],
  );
  return (
    <View>
      {visiblePriceFilters(filters, combined).map((filter) => (
        <View style={styles.row} key={filter.key}>
          <Pressable
            style={styles.hit}
            disabled={disabled}
            accessibilityRole="checkbox"
            accessibilityState={{ checked: filter.selected }}
            accessibilityLabel={filter.label}
            accessibilityHint={filter.tooltip}
            onPress={() =>
              onChange(
                filters.map((current) =>
                  current.key === filter.key
                    ? {
                        ...current,
                        selected: !current.selected,
                        min: current.min ?? current.value,
                      }
                    : current,
                ),
              )
            }
          >
            <View
              style={[
                styles.checkbox,
                filter.selected && {
                  backgroundColor: theme.accent,
                  borderColor: theme.accent,
                },
              ]}
            />
            <Text
              numberOfLines={1}
              style={[styles.label, !filter.selected && styles.dim]}
            >
              {filter.kind === 'property'
                ? `${filter.label}: ${filter.value}`
                : filter.label}
            </Text>
          </Pressable>
          {filter.value !== null ? (
            <MinimumInput
              filter={filter}
              disabled={disabled}
              onValidity={callbacks.get(filter.key)!}
              onChange={(min) =>
                onChange(
                  filters.map((current) =>
                    current.key === filter.key
                      ? { ...current, min, max: null, selected: true }
                      : current,
                  ),
                )
              }
            />
          ) : null}
          {filter.tooltip ? (
            <Text
              style={styles.info}
              accessibilityLabel={filter.tooltip}
              {...{ tooltip: filter.tooltip }}
            >
              ⓘ
            </Text>
          ) : null}
        </View>
      ))}
    </View>
  );
}

export function PriceFilterActions({
  filters,
  onChange,
  combined,
  disabled = false,
  onCombinedChange,
}: Props & { onCombinedChange: (value: boolean) => void }) {
  const theme = useTheme();
  const [open, setOpen] = useState(false);
  const preset = matchingMinimumPreset(visiblePriceFilters(filters, combined));
  const group = filters.find((filter) => filter.combinedKeys?.length);
  useEffect(() => {
    if (disabled) setOpen(false);
  }, [disabled]);
  return (
    <View style={styles.actions}>
      {group ? (
        <Pressable
          style={[styles.button, combined && { borderColor: theme.accent }]}
          disabled={disabled}
          accessibilityLabel="Combine fire, cold and lightning resistance"
          accessibilityState={{ selected: combined }}
          onPress={() => {
            setOpen(false);
            onChange(resistanceMode(filters, !combined));
            onCombinedChange(!combined);
          }}
        >
          <Text style={styles.buttonText}>Total res</Text>
        </Pressable>
      ) : null}
      {filters.length ? (
        <View style={styles.picker}>
          <Pressable
            disabled={disabled}
            style={styles.button}
            accessibilityLabel="Search minimum preset"
            accessibilityState={{ expanded: open }}
            onPress={() => setOpen(!open)}
          >
            <Text style={styles.buttonText}>
              {preset === null ? 'Custom' : preset === 0 ? 'Exact' : `−${preset}%`} ▾
            </Text>
          </Pressable>
          {open ? (
            <View style={styles.menu}>
              {[0, 5, 10, 20].map((percent) => (
                <Pressable
                  key={percent}
                  accessibilityLabel={`Apply ${percent === 0 ? 'Exact' : `−${percent}%`} minimum preset`}
                  style={styles.option}
                  onPress={() => {
                    onChange(applyMinimumPreset(filters, percent));
                    setOpen(false);
                  }}
                >
                  <Text style={styles.buttonText}>
                    {percent === 0 ? 'Exact' : `−${percent}%`}
                    {preset === percent ? '  ✓' : ''}
                  </Text>
                </Pressable>
              ))}
            </View>
          ) : null}
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    paddingHorizontal: 8,
    paddingVertical: 3,
    marginBottom: 2,
    backgroundColor: surfaces.chip,
    borderWidth: 1,
    borderColor: surfaces.chipBorder,
    borderRadius: radii.input,
  },
  hit: { flex: 1, flexDirection: 'row', gap: 8, alignItems: 'center', minHeight: 21 },
  checkbox: {
    width: 12,
    height: 12,
    borderRadius: 3,
    borderWidth: 1,
    borderColor: borders.checkbox,
  },
  label: { flex: 1, fontFamily: fonts.sans, fontSize: 12, color: palette.primary },
  dim: { color: palette.muted },
  input: {
    width: 64,
    color: palette.primary,
    backgroundColor: surfaces.sunkenStrong,
    borderWidth: 1,
    borderColor: borders.input,
    borderRadius: radii.input,
    fontFamily: fonts.mono,
    fontSize: 11,
    paddingHorizontal: 6,
    paddingVertical: 2,
    textAlign: 'right',
  },
  invalid: { borderColor: '#d19a66' },
  info: { color: palette.secondary, fontSize: 12 },
  actions: { flexDirection: 'row', alignItems: 'center', gap: 6, zIndex: 30 },
  picker: { position: 'relative', zIndex: 30 },
  button: {
    borderWidth: 1,
    borderColor: borders.standard,
    borderRadius: radii.field,
    paddingHorizontal: 8,
    paddingVertical: 6,
    backgroundColor: surfaces.chip,
  },
  buttonText: { fontFamily: fonts.sans, fontSize: 11, color: palette.primary },
  menu: {
    position: 'absolute',
    bottom: 33,
    right: 0,
    minWidth: 104,
    padding: 4,
    backgroundColor: surfaces.card,
    borderWidth: 1,
    borderColor: borders.strong,
    borderRadius: radii.field,
  },
  option: { paddingHorizontal: spacing.md, paddingVertical: 7 },
});
