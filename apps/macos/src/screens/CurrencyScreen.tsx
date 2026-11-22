import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  ActivityIndicator,
  FlatList,
  Image,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import Svg, { Defs, LinearGradient, Path, Stop } from 'react-native-svg';
import type {
  HistoryPoint,
  ItemHistoryPoint,
  MarketPeriod,
  MarketRow,
  MarketSnapshot,
} from '@vaaluation/trade-client';
import {
  CHAOS_ID,
  DIVINE_ID,
  MARKET_CATEGORIES,
  inUnit,
  loadItemHistory,
  loadMarket,
} from '@vaaluation/trade-client';
import { resolveLeagueId } from '@vaaluation/shared-types';
import {
  alpha,
  borders,
  fonts,
  radii,
  semantic,
  spacing,
  surfaces,
  text as palette,
  type as scale,
  useTheme,
} from '@vaaluation/ui';
import type { Theme } from '@vaaluation/ui';
import { useSettings } from '../state/SettingsContext';
import { useLeagues } from '../hooks/useLeagues';
import { getCurrencyHistoryClient, getMarketCatalog } from '../services/trade';
import { HistoryPlot } from '../components/CurrencyHistoryPanel';

const EXALTED_ID = 'Metadata/Items/Currency/CurrencyAddModToRare';
const MIRROR_ID = 'Metadata/Items/Currency/CurrencyDuplicate';
const FAVOURITE_UNITS: readonly { id: string; label: string }[] = [
  { id: 'auto', label: 'Auto (Chaos or Divine)' },
  { id: CHAOS_ID, label: 'Chaos Orb' },
  { id: DIVINE_ID, label: 'Divine Orb' },
  { id: EXALTED_ID, label: 'Exalted Orb' },
  { id: MIRROR_ID, label: 'Mirror of Kalandra' },
];

type Sort = 'price' | 'volume' | 'change' | 'name';

/** Compact amounts the way traders say them: 1.77K, 436, 9.71, 0.82. */
export function formatAmount(value: number): string {
  if (value >= 100_000) return `${(value / 1000).toFixed(0)}K`;
  if (value >= 10_000) return `${trim((value / 1000).toFixed(1))}K`;
  if (value >= 1000) return `${trim((value / 1000).toFixed(2))}K`;
  if (value >= 100) return value.toFixed(0);
  if (value >= 10) return trim(value.toFixed(1));
  if (value >= 1) return trim(value.toFixed(2));
  if (value >= 0.01) return trim(value.toFixed(3));
  return value.toPrecision(2);
}
const trim = (text: string) => text.replace(/\.?0+$/, '');
const formatVolume = (value: number) =>
  value >= 10_000 ? `${trim((value / 1000).toFixed(1))}K` : value.toLocaleString();
const formatChange = (value: number | null) =>
  value === null ? '—' : `${value >= 0 ? '+' : ''}${trim(value.toFixed(1))}%`;

export function CurrencyScreen() {
  const theme = useTheme();
  const styles = useMemo(() => makeStyles(theme), [theme]);
  const { settings } = useSettings();
  const { leagues } = useLeagues();
  const league = resolveLeagueId(leagues, settings.leagueId);

  const [period, setPeriod] = useState<MarketPeriod>('24h');
  const [unit, setUnit] = useState('auto');
  const [unitOpen, setUnitOpen] = useState(false);
  const [category, setCategory] = useState('All');
  const [query, setQuery] = useState('');
  const [sort, setSort] = useState<Sort>('price');
  const [descending, setDescending] = useState(true);
  const [snapshot, setSnapshot] = useState<MarketSnapshot | null>(null);
  const [chaosIcon, setChaosIcon] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [open, setOpen] = useState<MarketRow | null>(null);
  const revision = useRef(0);

  const refresh = useCallback(async () => {
    if (league === null) return;
    const requested = ++revision.current;
    setLoading(true);
    setError(null);
    try {
      const catalog = await getMarketCatalog();
      const result = await loadMarket(
        getCurrencyHistoryClient(),
        catalog,
        league,
        period,
        () => requested !== revision.current,
      );
      if (requested !== revision.current) return;
      setSnapshot(result);
      setChaosIcon(catalog.item(CHAOS_ID).icon);
    } catch (cause: unknown) {
      if (requested === revision.current)
        setError(cause instanceof Error ? cause.message : 'Prices could not be loaded.');
    } finally {
      if (requested === revision.current) setLoading(false);
    }
  }, [league, period]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  /** Chaos value of every unit; Chaos itself is 1. */
  const rates = useMemo(() => {
    const map = new Map<string, number>([[CHAOS_ID, 1]]);
    for (const row of snapshot?.rows ?? []) map.set(row.item.id, row.chaos);
    return map;
  }, [snapshot]);
  const iconOf = useCallback(
    (id: string) =>
      id === CHAOS_ID
        ? chaosIcon
        : (snapshot?.rows.find((row) => row.item.id === id)?.item.icon ?? null),
    [snapshot, chaosIcon],
  );
  const nameOf = useCallback(
    (id: string) =>
      id === CHAOS_ID
        ? 'Chaos Orb'
        : (snapshot?.rows.find((row) => row.item.id === id)?.item.name ?? 'Chaos Orb'),
    [snapshot],
  );

  const counts = useMemo(() => {
    const map = new Map<string, number>();
    for (const row of snapshot?.rows ?? [])
      map.set(row.item.category, (map.get(row.item.category) ?? 0) + 1);
    return map;
  }, [snapshot]);

  const rows = useMemo(() => {
    const needle = query.trim().toLowerCase();
    const filtered = (snapshot?.rows ?? []).filter(
      (row) =>
        (category === 'All' || row.item.category === category) &&
        (needle === '' || row.item.name.toLowerCase().includes(needle)),
    );
    const key = (row: MarketRow) =>
      sort === 'volume'
        ? row.volume
        : sort === 'change'
          ? (row.change ?? Number.NEGATIVE_INFINITY)
          : row.chaos;
    const ordered =
      sort === 'name'
        ? [...filtered].sort((a, b) => a.item.name.localeCompare(b.item.name))
        : [...filtered].sort((a, b) => key(a) - key(b));
    return descending ? ordered.reverse() : ordered;
  }, [snapshot, category, query, sort, descending]);

  const otherUnits = useMemo(
    () =>
      (snapshot?.rows ?? [])
        .filter(
          (row) =>
            row.item.category === 'Currency' &&
            !FAVOURITE_UNITS.some((favourite) => favourite.id === row.item.id),
        )
        .sort((a, b) => a.item.name.localeCompare(b.item.name)),
    [snapshot],
  );
  const unitLabel =
    unit === 'auto'
      ? 'Auto'
      : (FAVOURITE_UNITS.find((entry) => entry.id === unit)?.label ?? nameOf(unit));

  const chooseSort = (next: Sort) => {
    setDescending(next === sort ? !descending : next !== 'name');
    setSort(next);
  };

  const updated =
    snapshot !== null
      ? new Date(snapshot.latestHour + 3_600_000).toLocaleTimeString(undefined, {
          hour: '2-digit',
          minute: '2-digit',
        })
      : null;

  return (
    <View style={styles.container}>
      <View style={styles.header}>
        <View style={styles.headerText}>
          <Text style={styles.heading}>Currency</Text>
          <Text style={styles.meta} numberOfLines={1}>
            {error ??
              (snapshot === null
                ? 'Loading completed trades from GGG…'
                : `${snapshot.league} · ${snapshot.rows.length.toLocaleString()} items · completed trades to ${updated}${snapshot.failedHours > 0 ? ` · ${snapshot.failedHours} hour(s) unavailable` : ''}`)}
          </Text>
        </View>
        <Text style={styles.priceIn}>Price in</Text>
        <View style={styles.selectWrap}>
          <Pressable
            style={[styles.select, unitOpen && styles.selectOpen]}
            onPress={() => setUnitOpen((value) => !value)}
            accessibilityLabel={`Show prices in ${unitLabel}`}
          >
            {unit !== 'auto' && iconOf(unit) ? (
              <Image source={{ uri: iconOf(unit)! }} style={styles.selectIcon} />
            ) : null}
            <Text style={styles.selectText} numberOfLines={1}>
              {unitLabel}
            </Text>
            <Text style={styles.caret}>▾</Text>
          </Pressable>
          {unitOpen ? (
            <ScrollView style={styles.menu}>
              {[
                ...FAVOURITE_UNITS,
                ...otherUnits.map((row) => ({ id: row.item.id, label: row.item.name })),
              ]
                .filter(
                  (entry) =>
                    entry.id === 'auto' || entry.id === CHAOS_ID || rates.has(entry.id),
                )
                .map((entry) => (
                  <Pressable
                    key={entry.id}
                    style={[styles.option, unit === entry.id && styles.optionActive]}
                    onPress={() => {
                      setUnit(entry.id);
                      setUnitOpen(false);
                    }}
                  >
                    {entry.id !== 'auto' && iconOf(entry.id) ? (
                      <Image
                        source={{ uri: iconOf(entry.id)! }}
                        style={styles.optionIcon}
                      />
                    ) : (
                      <View style={styles.optionIcon} />
                    )}
                    <Text
                      style={[
                        styles.optionText,
                        unit === entry.id && styles.optionTextActive,
                      ]}
                      numberOfLines={1}
                    >
                      {entry.label}
                    </Text>
                    {unit === entry.id ? <Text style={styles.tick}>✓</Text> : null}
                  </Pressable>
                ))}
            </ScrollView>
          ) : null}
        </View>
        {(['24h', '7d'] as const).map((value) => (
          <Pressable
            key={value}
            style={[styles.chip, period === value && styles.chipActive]}
            onPress={() => setPeriod(value)}
            accessibilityLabel={`Sparklines over ${value === '24h' ? '24 hours' : '7 days'}`}
          >
            <Text style={[styles.chipText, period === value && styles.chipTextActive]}>
              {value}
            </Text>
          </Pressable>
        ))}
        <Pressable
          style={styles.refresh}
          onPress={() => void refresh()}
          disabled={loading}
        >
          {loading ? (
            <ActivityIndicator size="small" color={theme.actionText} />
          ) : (
            <Text style={styles.refreshText}>Refresh</Text>
          )}
        </Pressable>
      </View>

      {open !== null && snapshot !== null ? (
        <MarketDetail
          row={snapshot.rows.find((row) => row.item.id === open.item.id) ?? open}
          league={snapshot.league}
          unit={unit}
          rates={rates}
          iconOf={iconOf}
          nameOf={nameOf}
          onBack={() => setOpen(null)}
          styles={styles}
        />
      ) : (
        <View style={styles.body}>
          <ScrollView
            style={styles.categories}
            contentContainerStyle={styles.categoriesContent}
          >
            {[{ id: 'All', label: 'All items' }, ...MARKET_CATEGORIES]
              .filter((entry) => entry.id === 'All' || (counts.get(entry.id) ?? 0) > 0)
              .map((entry) => {
                const active = category === entry.id;
                const count =
                  entry.id === 'All'
                    ? (snapshot?.rows.length ?? 0)
                    : (counts.get(entry.id) ?? 0);
                return (
                  <Pressable
                    key={entry.id}
                    style={[styles.category, active && styles.categoryActive]}
                    onPress={() => setCategory(entry.id)}
                  >
                    <Text
                      style={[styles.categoryText, active && styles.categoryTextActive]}
                      numberOfLines={1}
                    >
                      {entry.label}
                    </Text>
                    <Text
                      style={[styles.categoryCount, active && styles.categoryTextActive]}
                    >
                      {count.toLocaleString()}
                    </Text>
                  </Pressable>
                );
              })}
          </ScrollView>

          <View style={styles.tableCard}>
            <TextInput
              style={styles.search}
              value={query}
              onChangeText={setQuery}
              placeholder="Search items…"
              placeholderTextColor={palette.faint}
            />
            <View style={styles.tableHead}>
              <View style={styles.colIcon} />
              <SortHead
                label="Item"
                active={sort === 'name'}
                descending={descending}
                onPress={() => chooseSort('name')}
                style={styles.colName}
                styles={styles}
              />
              <SortHead
                label="Price"
                active={sort === 'price'}
                descending={descending}
                onPress={() => chooseSort('price')}
                style={styles.colPrice}
                styles={styles}
                align="right"
              />
              <SortHead
                label="Traded 24h"
                active={sort === 'volume'}
                descending={descending}
                onPress={() => chooseSort('volume')}
                style={styles.colVolume}
                styles={styles}
                align="right"
              />
              <Text style={[styles.headText, styles.colSpark, styles.center]}>
                {period === '7d' ? '7 DAYS' : '24 HOURS'}
              </Text>
              <SortHead
                label="Change"
                active={sort === 'change'}
                descending={descending}
                onPress={() => chooseSort('change')}
                style={styles.colChange}
                styles={styles}
                align="right"
              />
            </View>
            {snapshot === null && loading ? (
              <View style={styles.placeholder}>
                <ActivityIndicator color={palette.faint} />
                <Text style={styles.placeholderText}>
                  Loading the last 24 hours of trades…
                </Text>
              </View>
            ) : (
              <FlatList
                data={rows}
                keyExtractor={(row) => row.item.id}
                initialNumToRender={20}
                windowSize={8}
                ListEmptyComponent={
                  <Text style={[styles.placeholderText, styles.placeholder]}>
                    {query.trim() !== ''
                      ? `Nothing traded matches “${query.trim()}”.`
                      : 'Nothing traded in this category in the last 24 hours.'}
                  </Text>
                }
                renderItem={({ item: row }) => (
                  <MarketRowView
                    row={row}
                    price={inUnit(row.chaos, unit, rates)}
                    iconOf={iconOf}
                    onPress={() => setOpen(row)}
                    styles={styles}
                  />
                )}
              />
            )}
          </View>
        </View>
      )}
      <Text style={styles.footnote}>
        Prices are GGG's completed Currency Exchange trades over the last 24 hours,
        volume-weighted. Item names: RePoE game data. Thinly traded items move more.
      </Text>
    </View>
  );
}

type Styles = ReturnType<typeof makeStyles>;

function SortHead({
  label,
  active,
  descending,
  onPress,
  style,
  styles,
  align,
}: {
  label: string;
  active: boolean;
  descending: boolean;
  onPress: () => void;
  style: object;
  styles: Styles;
  align?: 'right';
}) {
  return (
    <Pressable style={style} onPress={onPress} accessibilityLabel={`Sort by ${label}`}>
      <Text
        style={[
          styles.headText,
          active && styles.headTextActive,
          align === 'right' && styles.right,
        ]}
      >
        {label.toUpperCase()}
        {active ? (descending ? ' ▼' : ' ▲') : ''}
      </Text>
    </Pressable>
  );
}

function MarketRowView({
  row,
  price,
  iconOf,
  onPress,
  styles,
}: {
  row: MarketRow;
  price: { amount: number; unit: string } | null;
  iconOf: (id: string) => string | null;
  onPress: () => void;
  styles: Styles;
}) {
  const [hovered, setHovered] = useState(false);
  const unitIcon = price ? iconOf(price.unit) : null;
  return (
    <Pressable
      style={[styles.row, hovered && styles.rowHover]}
      onPress={onPress}
      onHoverIn={() => setHovered(true)}
      onHoverOut={() => setHovered(false)}
      accessibilityLabel={`${row.item.name}, ${price ? formatAmount(price.amount) : 'no price'}, ${formatChange(row.change)}`}
    >
      <View style={[styles.colIcon, styles.iconTile]}>
        {row.item.icon ? (
          <Image source={{ uri: row.item.icon }} style={styles.icon} />
        ) : null}
      </View>
      <Text style={[styles.name, styles.colName]} numberOfLines={1}>
        {row.item.name}
      </Text>
      <View style={[styles.colPrice, styles.priceCell]}>
        <Text style={styles.price}>{price ? formatAmount(price.amount) : '—'}</Text>
        {unitIcon ? (
          <Image source={{ uri: unitIcon }} style={styles.unitIcon} />
        ) : price ? (
          <Text style={styles.unitText}>{price.unit === DIVINE_ID ? 'div' : 'c'}</Text>
        ) : null}
      </View>
      <Text style={[styles.volume, styles.colVolume]}>{formatVolume(row.volume)}</Text>
      <View style={styles.colSpark}>
        <Sparkline series={row.series} change={row.change} width={112} height={26} />
      </View>
      <Text
        style={[
          styles.change,
          styles.colChange,
          row.change === null
            ? styles.changeNone
            : row.change >= 0
              ? styles.changeUp
              : styles.changeDown,
        ]}
      >
        {formatChange(row.change)}
      </Text>
    </Pressable>
  );
}

/** A small price line: olive when rising, ember when falling (red is the brand). */
function Sparkline({
  series,
  change,
  width,
  height,
}: {
  series: readonly (number | null)[];
  change: number | null;
  width: number;
  height: number;
}) {
  const priced = series
    .map((value, index) => ({ value, index }))
    .filter((point): point is { value: number; index: number } => point.value !== null);
  if (priced.length < 2)
    return (
      <View style={{ height, justifyContent: 'center' }}>
        <View style={{ height: 1, backgroundColor: borders.hairline }} />
      </View>
    );
  const low = Math.min(...priced.map((point) => point.value));
  const high = Math.max(...priced.map((point) => point.value));
  const span = Math.max(high - low, high * 0.001);
  const x = (index: number) => (index / Math.max(1, series.length - 1)) * width;
  const y = (value: number) => height - 2 - ((value - low) / span) * (height - 4);
  const line = priced
    .map(
      (point, index) =>
        `${index === 0 ? 'M' : 'L'}${x(point.index).toFixed(1)},${y(point.value).toFixed(1)}`,
    )
    .join(' ');
  const area = `${line} L${x(priced.at(-1)!.index).toFixed(1)},${height} L${x(priced[0]!.index).toFixed(1)},${height} Z`;
  const color =
    change === null ? palette.muted : change >= 0 ? semantic.up : semantic.down;
  const id = `spark-${color.replace('#', '')}`;
  return (
    <Svg width={width} height={height}>
      <Defs>
        <LinearGradient id={id} x1="0" y1="0" x2="0" y2="1">
          <Stop offset="0" stopColor={color} stopOpacity={0.28} />
          <Stop offset="1" stopColor={color} stopOpacity={0} />
        </LinearGradient>
      </Defs>
      <Path d={area} fill={`url(#${id})`} />
      <Path d={line} stroke={color} strokeWidth={1.6} fill="none" />
    </Svg>
  );
}

function MarketDetail({
  row,
  league,
  unit,
  rates,
  iconOf,
  nameOf,
  onBack,
  styles,
}: {
  row: MarketRow;
  league: string;
  unit: string;
  rates: ReadonlyMap<string, number>;
  iconOf: (id: string) => string | null;
  nameOf: (id: string) => string;
  onBack: () => void;
  styles: Styles;
}) {
  const [period, setPeriod] = useState<MarketPeriod>('7d');
  const [points, setPoints] = useState<ItemHistoryPoint[] | null>(null);
  const [failed, setFailed] = useState(0);
  const [error, setError] = useState<string | null>(null);
  // The chart needs a fixed unit: Auto resolves to Divine or Chaos for this item.
  const resolved = inUnit(row.chaos, unit, rates)?.unit ?? CHAOS_ID;
  const chartUnit = resolved === row.item.id ? CHAOS_ID : resolved;
  const price = inUnit(row.chaos, chartUnit, rates);

  useEffect(() => {
    let cancelled = false;
    setPoints(null);
    setError(null);
    loadItemHistory(
      getCurrencyHistoryClient(),
      league,
      row.item.id,
      chartUnit,
      period,
      () => cancelled,
    )
      .then((result) => {
        if (cancelled) return;
        setPoints(result.points);
        setFailed(result.failedHours);
      })
      .catch((cause: unknown) => {
        if (!cancelled)
          setError(cause instanceof Error ? cause.message : 'History unavailable.');
      });
    return () => {
      cancelled = true;
    };
  }, [league, row.item.id, chartUnit, period]);

  const divine = rates.get(DIVINE_ID);
  const category =
    MARKET_CATEGORIES.find((entry) => entry.id === row.item.category)?.label ??
    row.item.category;
  const plotted: HistoryPoint[] = (points ?? []).map((point) => ({
    at: point.at,
    rate: point.value,
    giveVolume: point.volume,
    wantVolume: null,
  }));
  const unitIcon = iconOf(chartUnit);

  return (
    <ScrollView style={styles.detail} contentContainerStyle={styles.detailContent}>
      <Pressable style={styles.back} onPress={onBack}>
        <Text style={styles.backText}>← All items</Text>
      </Pressable>
      <View style={styles.detailHeader}>
        <View style={styles.detailTile}>
          {row.item.icon ? (
            <Image source={{ uri: row.item.icon }} style={styles.detailIcon} />
          ) : null}
        </View>
        <View style={styles.detailTitle}>
          <Text style={styles.detailName} numberOfLines={1}>
            {row.item.name}
          </Text>
          <Text style={styles.detailCategory}>{category.toUpperCase()}</Text>
        </View>
        {price ? (
          <View style={styles.detailPrice}>
            <View style={styles.priceCell}>
              <Text style={styles.detailAmount}>{formatAmount(price.amount)}</Text>
              {unitIcon ? (
                <Image source={{ uri: unitIcon }} style={styles.detailUnit} />
              ) : null}
            </View>
            <Text style={styles.detailCaption}>in {nameOf(chartUnit)}</Text>
          </View>
        ) : null}
      </View>
      <Text style={styles.stats}>
        {formatAmount(row.chaos)} Chaos
        {divine ? ` · ${formatAmount(row.chaos / divine)} Divine` : ''} ·{' '}
        {row.volume.toLocaleString()} traded in 24h · {formatChange(row.change)} over the
        table period
      </Text>
      <View style={styles.detailChips}>
        {(['24h', '7d', '30d'] as const).map((value) => (
          <Pressable
            key={value}
            style={[styles.chip, period === value && styles.chipActive]}
            onPress={() => setPeriod(value)}
          >
            <Text style={[styles.chipText, period === value && styles.chipTextActive]}>
              {value}
            </Text>
          </Pressable>
        ))}
      </View>
      {error !== null ? (
        <Text style={styles.placeholderText}>{error}</Text>
      ) : points === null ? (
        <View style={styles.placeholder}>
          <ActivityIndicator color={palette.faint} />
          <Text style={styles.placeholderText}>Loading history…</Text>
        </View>
      ) : (
        <HistoryPlot points={plotted} unit={nameOf(chartUnit)} />
      )}
      <Text style={styles.footnote}>
        {period === '24h'
          ? 'Each point is one hour of GGG completed trades.'
          : period === '7d'
            ? 'Each point combines four sampled hours of one day.'
            : 'Each point is one sampled hour per day; quiet days stay gaps.'}
        {failed > 0 ? ` ${failed} hour(s) could not be loaded.` : ''}
      </Text>
    </ScrollView>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    container: {
      flex: 1,
      paddingHorizontal: spacing.h5,
      paddingTop: spacing.h3,
      paddingBottom: spacing.xl,
    },
    header: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, zIndex: 10 },
    headerText: { flex: 1 },
    heading: {
      fontFamily: fonts.sans,
      fontSize: scale.xxl,
      fontWeight: '700',
      color: palette.primary,
      letterSpacing: -0.4,
    },
    meta: {
      fontFamily: fonts.sans,
      fontSize: scale.caption,
      color: palette.muted,
      marginTop: 2,
    },
    priceIn: { fontFamily: fonts.sans, fontSize: scale.caption, color: palette.dim },
    selectWrap: { position: 'relative', zIndex: 20 },
    select: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: spacing.sm,
      minWidth: 150,
      maxWidth: 220,
      paddingHorizontal: spacing.xl,
      paddingVertical: 6,
      borderRadius: radii.field,
      borderWidth: 1,
      borderColor: borders.strong,
      backgroundColor: surfaces.raised,
    },
    selectOpen: { borderColor: alpha(theme.accent, 0.6) },
    selectIcon: { width: 18, height: 18 },
    selectText: {
      flex: 1,
      fontFamily: fonts.sans,
      fontSize: scale.ui,
      color: palette.body,
    },
    caret: { color: palette.muted, fontSize: scale.caption },
    menu: {
      position: 'absolute',
      top: 38,
      right: 0,
      width: 250,
      maxHeight: 360,
      borderRadius: radii.card,
      borderWidth: 1,
      borderColor: borders.stronger,
      backgroundColor: '#1d1719',
      padding: spacing.xs,
    },
    option: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: spacing.md,
      paddingHorizontal: spacing.md,
      paddingVertical: 6,
      borderRadius: radii.input,
    },
    optionActive: { backgroundColor: alpha(theme.accent, 0.22) },
    optionIcon: { width: 18, height: 18 },
    optionText: {
      flex: 1,
      fontFamily: fonts.sans,
      fontSize: scale.ui,
      color: palette.body,
    },
    optionTextActive: { color: palette.primary, fontWeight: '600' },
    tick: { color: theme.accentText, fontSize: scale.caption },
    chip: {
      paddingHorizontal: spacing.xl,
      paddingVertical: 5,
      borderRadius: radii.keycap,
      borderWidth: 1,
      borderColor: borders.standard,
      backgroundColor: surfaces.chip,
    },
    chipActive: {
      backgroundColor: alpha(theme.accent, 0.28),
      borderColor: alpha(theme.accent, 0.55),
    },
    chipText: { fontFamily: fonts.mono, fontSize: scale.mono, color: palette.secondary },
    chipTextActive: { color: '#ffdde1' },
    refresh: {
      minWidth: 82,
      alignItems: 'center',
      paddingHorizontal: spacing.h1,
      paddingVertical: 7,
      borderRadius: radii.field,
      backgroundColor: theme.action,
    },
    refreshText: {
      fontFamily: fonts.sans,
      fontSize: scale.ui,
      fontWeight: '600',
      color: theme.actionText,
    },
    body: { flex: 1, flexDirection: 'row', gap: spacing.xl, marginTop: spacing.h1 },
    categories: {
      width: 200,
      flexGrow: 0,
      borderRadius: radii.card,
      borderWidth: 1,
      borderColor: surfaces.cardBorder,
      backgroundColor: surfaces.card,
    },
    categoriesContent: { padding: spacing.sm },
    category: {
      flexDirection: 'row',
      alignItems: 'center',
      paddingHorizontal: spacing.xl,
      paddingVertical: 8,
      borderRadius: radii.button,
      borderWidth: 1,
      borderColor: 'transparent',
      marginBottom: 2,
    },
    categoryActive: {
      backgroundColor: alpha(theme.accent, 0.24),
      borderColor: alpha(theme.accent, 0.5),
    },
    categoryText: {
      flex: 1,
      fontFamily: fonts.sans,
      fontSize: scale.ui,
      color: palette.secondary,
    },
    categoryTextActive: { color: '#ffdde1', fontWeight: '600' },
    categoryCount: {
      fontFamily: fonts.mono,
      fontSize: scale.label,
      color: palette.faint,
    },
    tableCard: {
      flex: 1,
      borderRadius: radii.card,
      borderWidth: 1,
      borderColor: surfaces.cardBorder,
      backgroundColor: surfaces.card,
      padding: spacing.xl,
      paddingBottom: spacing.sm,
    },
    search: {
      fontFamily: fonts.sans,
      fontSize: scale.ui,
      color: palette.primary,
      borderRadius: radii.field,
      borderWidth: 1,
      borderColor: borders.input,
      backgroundColor: surfaces.sunkenStrong,
      paddingHorizontal: spacing.xl,
      paddingVertical: 7,
      marginBottom: spacing.lg,
    },
    tableHead: {
      flexDirection: 'row',
      alignItems: 'center',
      paddingHorizontal: spacing.md,
      paddingBottom: spacing.sm,
      borderBottomWidth: 1,
      borderBottomColor: borders.hairline,
    },
    headText: {
      fontFamily: fonts.mono,
      fontSize: 9.5,
      letterSpacing: 1.1,
      color: palette.faint,
    },
    headTextActive: { color: palette.body },
    right: { textAlign: 'right' },
    center: { textAlign: 'center' },
    colIcon: { width: 32 },
    colName: { flex: 1, paddingLeft: spacing.xl },
    colPrice: { width: 118 },
    colVolume: { width: 90 },
    colSpark: { width: 124, alignItems: 'center', paddingHorizontal: 6 },
    colChange: { width: 72 },
    row: {
      flexDirection: 'row',
      alignItems: 'center',
      paddingHorizontal: spacing.md,
      paddingVertical: 7,
      marginTop: 4,
      borderRadius: radii.field,
      borderWidth: 1,
      borderColor: surfaces.chipBorder,
      backgroundColor: surfaces.chip,
    },
    rowHover: { backgroundColor: surfaces.hoverStrong, borderColor: borders.strong },
    iconTile: {
      height: 32,
      borderRadius: radii.keycap,
      backgroundColor: surfaces.sunkenStrong,
      alignItems: 'center',
      justifyContent: 'center',
    },
    icon: { width: 28, height: 28 },
    name: {
      fontFamily: fonts.sans,
      fontSize: scale.body,
      fontWeight: '600',
      color: palette.primary,
    },
    priceCell: {
      flexDirection: 'row',
      alignItems: 'center',
      justifyContent: 'flex-end',
      gap: 5,
    },
    price: {
      fontFamily: fonts.mono,
      fontSize: scale.body,
      fontWeight: '600',
      color: palette.primary,
    },
    unitIcon: { width: 18, height: 18 },
    unitText: { fontFamily: fonts.sans, fontSize: scale.caption, color: palette.dim },
    volume: {
      fontFamily: fonts.mono,
      fontSize: scale.mono,
      color: palette.secondary,
      textAlign: 'right',
    },
    change: {
      fontFamily: fonts.mono,
      fontSize: scale.mono,
      fontWeight: '600',
      textAlign: 'right',
    },
    changeUp: { color: semantic.upText },
    changeDown: { color: semantic.down },
    changeNone: { color: palette.faint },
    placeholder: {
      alignItems: 'center',
      justifyContent: 'center',
      padding: spacing.h6,
      gap: spacing.md,
    },
    placeholderText: {
      fontFamily: fonts.sans,
      fontSize: scale.ui,
      color: palette.dim,
      textAlign: 'center',
    },
    footnote: {
      fontFamily: fonts.sans,
      fontSize: scale.tiny,
      color: palette.faint,
      marginTop: spacing.md,
    },
    detail: {
      flex: 1,
      marginTop: spacing.h1,
      borderRadius: radii.card,
      borderWidth: 1,
      borderColor: surfaces.cardBorder,
      backgroundColor: surfaces.card,
    },
    detailContent: { padding: spacing.h3 },
    back: {
      alignSelf: 'flex-start',
      paddingHorizontal: spacing.md,
      paddingVertical: 4,
      borderRadius: radii.keycap,
      marginBottom: spacing.xl,
    },
    backText: { fontFamily: fonts.sans, fontSize: scale.ui, color: palette.secondary },
    detailHeader: { flexDirection: 'row', alignItems: 'center', gap: spacing.h1 },
    detailTile: {
      width: 60,
      height: 60,
      borderRadius: radii.panel,
      borderWidth: 1,
      borderColor: borders.standard,
      backgroundColor: surfaces.sunkenStrong,
      alignItems: 'center',
      justifyContent: 'center',
    },
    detailIcon: { width: 48, height: 48 },
    detailTitle: { flex: 1 },
    detailName: {
      fontFamily: fonts.sans,
      fontSize: scale.xl,
      fontWeight: '700',
      color: palette.primary,
    },
    detailCategory: {
      fontFamily: fonts.mono,
      fontSize: scale.label,
      letterSpacing: 1.2,
      color: theme.accentText,
      marginTop: 4,
    },
    detailPrice: { alignItems: 'flex-end' },
    detailAmount: {
      fontFamily: fonts.mono,
      fontSize: scale.display,
      fontWeight: '700',
      color: '#ffffff',
    },
    detailUnit: { width: 30, height: 30 },
    detailCaption: {
      fontFamily: fonts.sans,
      fontSize: scale.caption,
      color: palette.dim,
      marginTop: 2,
    },
    stats: {
      fontFamily: fonts.sans,
      fontSize: scale.ui,
      color: palette.secondary,
      marginTop: spacing.h1,
      marginBottom: spacing.xl,
    },
    detailChips: { flexDirection: 'row', gap: spacing.sm, marginBottom: spacing.lg },
  });
}
