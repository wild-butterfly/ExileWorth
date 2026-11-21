import React from 'react';
import { Text } from 'react-native';
import renderer, { act, type ReactTestRenderer } from 'react-test-renderer';
import type { ExchangeQuote } from '@vaaluation/trade-client';
import { ExchangePriceCard } from '../src/components/ExchangePriceCard';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
const quote: ExchangeQuote = {
  item: { id: 'betrayal-thread', name: 'Betrayal Thread' },
  source: 'bulk-exchange',
  perItemChaos: 0.0025,
  stackChaos: 0.05,
  quantity: 20,
  offers: [],
  rate: null,
  updatedAt: 1000,
  url: 'https://www.pathofexile.com/trade/exchange/Covens',
};
describe('Mac exchange quote', () => {
  let tree: ReactTestRenderer;
  afterEach(() => {
    if (tree) act(() => tree.unmount());
  });
  function text() {
    return tree.root
      .findAllByType(Text)
      .map((node) => node.props.children)
      .flat(Infinity)
      .join(' ');
  }
  it('labels the source Exchange and shows fractional per-item and stack values', () => {
    act(() => {
      tree = renderer.create(<ExchangePriceCard quote={quote} />);
    });
    expect(text()).toContain('Exchange');
    expect(text()).toContain('Bulk offers');
    expect(text()).toContain('0.0025 chaos each');
    expect(text()).toContain('0.05');
    expect(text()).toContain('not live in-game exchange orders');
  });
  it('shows an unavailable exchange rate without inventing a zero price', () => {
    act(() => {
      tree = renderer.create(
        <ExchangePriceCard quote={{ ...quote, perItemChaos: null, stackChaos: null }} />,
      );
    });
    expect(text()).toContain('No exchange offers in Chaos');
    expect(text()).not.toContain('0 chaos each');
    expect(text()).not.toContain('in stack');
  });
});
