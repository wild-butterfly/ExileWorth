import React from 'react';
import { StyleSheet, Text, View } from 'react-native';
import { formatExchangeAmount, type ExchangeQuote } from '@vaaluation/trade-client';
import { useTheme, spacing, radii, typography } from '@vaaluation/ui';

/** Exchange quotes never reuse the trade-listing headline or its labels. */
export function ExchangePriceCard({ quote }: { quote: ExchangeQuote }) {
  const theme = useTheme();
  return (
    <View style={styles.card}>
      <View style={styles.source}>
        <Text
          style={[styles.tag, { color: theme.accentText, borderColor: theme.accent }]}
        >
          Exchange
        </Text>
        <Text style={styles.detail}>Bulk offers · Chaos</Text>
      </View>
      <Text style={styles.price}>
        {quote.perItemChaos === null
          ? 'No exchange offers in Chaos'
          : `${formatExchangeAmount(quote.perItemChaos)} chaos each`}
      </Text>
      {quote.stackChaos !== null && quote.quantity > 1 ? (
        <Text style={styles.stack}>
          {quote.quantity} in stack ≈ {formatExchangeAmount(quote.stackChaos)} chaos
        </Text>
      ) : null}
      <Text style={styles.detail}>
        {quote.item.id === 'chaos'
          ? 'Chaos is the reference currency.'
          : `${quote.rate?.sampleSize ?? 0} sampled offers · sellers may require bulk purchases.`}
      </Text>
      <Text style={styles.detail}>
        Checked{' '}
        {new Date(quote.updatedAt).toLocaleTimeString([], {
          hour: '2-digit',
          minute: '2-digit',
        })}{' '}
        · cached up to 5 min
      </Text>
      <Text style={styles.detail}>
        Bulk asking rates, not live in-game exchange orders.
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  card: { padding: spacing.lg, gap: spacing.sm },
  source: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  tag: {
    fontSize: 11,
    fontWeight: '700',
    borderWidth: 1,
    borderRadius: radii.sm,
    paddingHorizontal: 7,
    paddingVertical: 3,
  },
  price: { color: '#f1e9eb', fontSize: 22, fontWeight: '600' },
  stack: { color: '#e0d0d4', fontSize: typography.sizeBody },
  detail: { color: '#a8999e', fontSize: 11 },
});
