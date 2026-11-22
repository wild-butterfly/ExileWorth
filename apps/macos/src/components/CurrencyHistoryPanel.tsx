import React, { useEffect, useMemo, useState } from 'react';
import {
  ActivityIndicator,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import Svg, { Circle, Line, Path, Text as SvgText } from 'react-native-svg';
import {
  TRACKED_CURRENCIES,
  type CurrencyHistory,
  type HistoryPeriod,
  type HistoryPoint,
} from '@vaaluation/trade-client';
import {
  borders,
  fonts,
  radii,
  semantic,
  surfaces,
  text as palette,
  useTheme,
} from '@vaaluation/ui';
import { getCurrencyHistoryClient } from '../services/trade';

const currencies = [{ id: 'chaos', label: 'Chaos Orb' }, ...TRACKED_CURRENCIES];
const labelOf = (id: string) =>
  currencies.find((currency) => currency.id === id)?.label ?? id;
const amount = (value: number) =>
  value.toLocaleString(undefined, { maximumSignificantDigits: 6 });
const stamp = (at: number) =>
  new Date(at).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });

export function HistoryPlot({
  points,
  unit,
}: {
  points: readonly HistoryPoint[];
  unit: string;
}) {
  const theme = useTheme();
  const [width, setWidth] = useState(500);
  const [selected, setSelected] = useState<number | null>(null);
  useEffect(() => setSelected(null), [points]);
  const valid = points.filter(
    (point): point is HistoryPoint & { rate: number } => point.rate !== null,
  );
  const height = 190,
    left = 62,
    right = 14,
    top = 14,
    bottom = 26;
  const plotWidth = Math.max(1, width - left - right),
    plotHeight = height - top - bottom;
  const low = Math.min(...valid.map((point) => point.rate)),
    high = Math.max(...valid.map((point) => point.rate));
  const pad = Math.max((high - low) * 0.12, high * 0.01, 0.000001);
  const min = Math.max(0, low - pad),
    max = high + pad;
  const first = points[0]?.at ?? 0,
    last = points.at(-1)?.at ?? first;
  const x = (at: number) => left + ((at - first) / Math.max(1, last - first)) * plotWidth;
  const y = (rate: number) => top + ((max - rate) / (max - min)) * plotHeight;
  let connected = false;
  const path = points
    .map((point) => {
      if (point.rate === null) {
        connected = false;
        return '';
      }
      const command = `${connected ? 'L' : 'M'}${x(point.at)},${y(point.rate)}`;
      connected = true;
      return command;
    })
    .join(' ');
  const active = selected !== null ? points[selected] : valid.at(-1);
  if (!valid.length)
    return (
      <Text style={styles.note}>
        No completed trades for this pair in the sampled hours. Try another pair or
        period.
      </Text>
    );
  return (
    <View>
      <Text style={styles.note}>{unit}</Text>
      <View
        onLayout={(event) => setWidth(event.nativeEvent.layout.width)}
        style={{ height }}
      >
        <Svg
          width={width}
          height={height}
          accessibilityLabel={`Currency history, ${valid.length} recorded hourly rates. Missing hours are gaps.`}
        >
          {[0, 0.5, 1].map((fraction) => {
            const value = min + (max - min) * fraction,
              yy = y(value);
            return (
              <React.Fragment key={fraction}>
                <Line
                  x1={left}
                  x2={width - right}
                  y1={yy}
                  y2={yy}
                  stroke={borders.hairline}
                />
                <SvgText
                  x={left - 8}
                  y={yy + 4}
                  fill={palette.secondary}
                  fontSize={10}
                  textAnchor="end"
                >
                  {value.toLocaleString(undefined, { maximumSignificantDigits: 4 })}
                </SvgText>
              </React.Fragment>
            );
          })}
          <Path d={path} stroke={theme.accentText} strokeWidth={2} fill="none" />
          {valid.map((point) => (
            <Circle
              key={point.at}
              cx={x(point.at)}
              cy={y(point.rate)}
              r={2.5}
              fill={theme.accentText}
            />
          ))}
          {active ? (
            <Line
              x1={x(active.at)}
              x2={x(active.at)}
              y1={top}
              y2={height - bottom}
              stroke={palette.dim}
              strokeDasharray="3 4"
            />
          ) : null}
          <SvgText x={left} y={height - 5} fill={palette.secondary} fontSize={10}>
            {new Date(first).toLocaleDateString(undefined, {
              month: 'short',
              day: 'numeric',
            })}
          </SvgText>
          <SvgText
            x={width - right}
            y={height - 5}
            fill={palette.secondary}
            fontSize={10}
            textAnchor="end"
          >
            {new Date(last).toLocaleDateString(undefined, {
              month: 'short',
              day: 'numeric',
            })}
          </SvgText>
        </Svg>
        {points.map((point, index) => {
          const before = index > 0 ? (x(points[index - 1]!.at) + x(point.at)) / 2 : left;
          const after =
            index < points.length - 1
              ? (x(point.at) + x(points[index + 1]!.at)) / 2
              : width - right;
          return (
            <Pressable
              key={point.at}
              style={{
                position: 'absolute',
                left: before,
                width: Math.max(1, after - before),
                top,
                height: plotHeight,
              }}
              accessibilityLabel={`${stamp(point.at)}: ${point.rate === null ? 'no trades' : amount(point.rate) + ' ' + unit}`}
              onHoverIn={() => setSelected(index)}
              onFocus={() => setSelected(index)}
              onPress={() => setSelected(index)}
            />
          );
        })}
      </View>
      <Text style={styles.inspection} accessibilityLiveRegion="polite">
        {active
          ? `${stamp(active.at)} · ${active.rate === null ? 'No trades recorded' : amount(active.rate) + ' ' + unit}${active.giveVolume !== null && active.wantVolume !== null ? ` · ${amount(active.giveVolume)} source / ${amount(active.wantVolume)} quote units traded` : active.giveVolume !== null ? ` · ${amount(active.giveVolume)} traded` : ''}`
          : ''}
      </Text>
    </View>
  );
}

export function CurrencyHistoryPanel({
  league,
  refreshKey,
}: {
  league: string | null;
  refreshKey: number;
}) {
  const theme = useTheme();
  const [give, setGive] = useState('divine');
  const [want, setWant] = useState('chaos');
  const [period, setPeriod] = useState<HistoryPeriod>('7d');
  const [picker, setPicker] = useState<'give' | 'want' | null>(null);
  const [search, setSearch] = useState('');
  const [history, setHistory] = useState<CurrencyHistory | null>(null);
  const [loading, setLoading] = useState(false);
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    let cancelled = false;
    setHistory(null);
    if (!league) {
      setLoading(false);
      return;
    }
    setLoading(true);
    getCurrencyHistoryClient()
      .history(league, give, want, period, () => cancelled)
      .then((result) => {
        if (!cancelled) setHistory(result);
      })
      .catch(() => {
        if (!cancelled) setHistory({ points: [], failedHours: 1 });
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [league, give, want, period, retry, refreshKey]);
  const available = useMemo(
    () =>
      currencies.filter(
        (currency) =>
          currency.label.toLowerCase().includes(search.trim().toLowerCase()) &&
          currency.id !== (picker === 'give' ? want : give),
      ),
    [search, picker, give, want],
  );
  const valid =
    history?.points.filter(
      (point): point is HistoryPoint & { rate: number } => point.rate !== null,
    ) ?? [];
  const latest = valid.at(-1);
  const change = valid.length >= 2 ? (latest!.rate / valid[0]!.rate - 1) * 100 : null;
  const unit = `${want} / ${labelOf(give)}`;
  return (
    <View style={styles.panel}>
      <View style={styles.toolbar}>
        <View style={styles.pair}>
          {(['give', 'want'] as const).map((side, index) => (
            <React.Fragment key={side}>
              {index ? <Text style={styles.note}>→</Text> : null}
              <Pressable
                style={styles.button}
                accessibilityLabel={`${side === 'give' ? 'From' : 'To'} currency`}
                accessibilityState={{ expanded: picker === side }}
                onPress={() => {
                  setPicker(picker === side ? null : side);
                  setSearch('');
                }}
              >
                <Text style={styles.buttonText}>
                  {labelOf(side === 'give' ? give : want)} ▾
                </Text>
              </Pressable>
            </React.Fragment>
          ))}
        </View>
        <View style={styles.pair}>
          {(['24h', '7d', '30d'] as const).map((range) => (
            <Pressable
              key={range}
              accessibilityLabel={`${range} history`}
              accessibilityState={{ selected: period === range }}
              style={[styles.button, period === range && { borderColor: theme.accent }]}
              onPress={() => {
                setPicker(null);
                setPeriod(range);
              }}
            >
              <Text style={styles.buttonText}>{range}</Text>
            </Pressable>
          ))}
        </View>
      </View>
      {picker ? (
        <View style={styles.menu}>
          <TextInput
            value={search}
            onChangeText={setSearch}
            placeholder="Search currencies…"
            placeholderTextColor={palette.dim}
            style={styles.search}
            accessibilityLabel="Search supported currencies"
            autoFocus
          />
          <ScrollView style={{ maxHeight: 180 }}>
            {available.map((currency) => (
              <Pressable
                key={currency.id}
                style={styles.option}
                onPress={() => {
                  if (picker === 'give') setGive(currency.id);
                  else setWant(currency.id);
                  setPicker(null);
                }}
              >
                <Text style={styles.buttonText}>{currency.label}</Text>
              </Pressable>
            ))}
            {!available.length ? (
              <Text style={styles.note}>No currencies match.</Text>
            ) : null}
          </ScrollView>
        </View>
      ) : null}
      <View style={styles.headline}>
        <Text style={styles.rate}>
          {latest ? amount(latest.rate) : '—'}{' '}
          <Text style={styles.rateUnit}>
            {want} / {labelOf(give)}
          </Text>
        </Text>
        {change !== null ? (
          <Text
            style={[
              styles.change,
              { color: change >= 0 ? semantic.upText : semantic.down },
            ]}
          >
            {change === 0 ? '' : change > 0 ? '↑ ' : '↓ '}
            {Math.abs(change).toFixed(1)}% between available samples
          </Text>
        ) : null}
      </View>
      {loading ? (
        <View style={styles.loading}>
          <ActivityIndicator size="small" color={theme.accentText} />
          <Text style={styles.note}>Loading completed-hour exchange history…</Text>
        </View>
      ) : history ? (
        <HistoryPlot points={history.points} unit={unit} />
      ) : (
        <Text style={styles.note}>Select a league to view history.</Text>
      )}
      {history?.failedHours ? (
        <Pressable
          onPress={() => setRetry((value) => value + 1)}
          accessibilityLabel="Retry unavailable history"
        >
          <Text style={styles.note}>
            Some history could not be loaded. Click to retry.
          </Text>
        </Pressable>
      ) : null}
      <Text style={styles.note}>
        GGG completed trades · volume-weighted hourly average
        {period !== '24h' ? ' · sampled daily' : ''} · gaps mean no recorded data
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  panel: {
    padding: 16,
    borderWidth: 1,
    borderColor: surfaces.cardBorder,
    borderRadius: radii.card,
    backgroundColor: surfaces.card,
    marginBottom: 20,
    gap: 12,
  },
  toolbar: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    gap: 12,
    flexWrap: 'wrap',
  },
  pair: { flexDirection: 'row', gap: 6, alignItems: 'center', flexWrap: 'wrap' },
  button: {
    backgroundColor: surfaces.chip,
    borderColor: borders.standard,
    borderWidth: 1,
    borderRadius: radii.field,
    paddingHorizontal: 9,
    paddingVertical: 7,
  },
  buttonText: { color: palette.primary, fontFamily: fonts.sans, fontSize: 12 },
  headline: { gap: 4 },
  rate: { color: palette.primary, fontFamily: fonts.mono, fontSize: 24 },
  rateUnit: { fontFamily: fonts.sans, fontSize: 12, color: palette.secondary },
  change: { fontFamily: fonts.sans, fontSize: 11 },
  note: {
    color: palette.secondary,
    fontFamily: fonts.sans,
    fontSize: 11,
    lineHeight: 16,
  },
  inspection: {
    color: palette.primary,
    fontFamily: fonts.mono,
    fontSize: 11,
    minHeight: 32,
  },
  menu: {
    borderWidth: 1,
    borderColor: borders.strong,
    borderRadius: radii.field,
    padding: 6,
    gap: 6,
  },
  search: {
    backgroundColor: surfaces.sunkenStrong,
    color: palette.primary,
    padding: 7,
    borderRadius: radii.input,
    fontSize: 12,
  },
  option: { padding: 8 },
  loading: { minHeight: 180, alignItems: 'center', justifyContent: 'center', gap: 12 },
});
