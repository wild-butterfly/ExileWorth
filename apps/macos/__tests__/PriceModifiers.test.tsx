import React from 'react';
import { TextInput } from 'react-native';
import renderer, { act, type ReactTestRenderer } from 'react-test-renderer';
import type { SelectableFilter } from '@vaaluation/trade-client';
import { PriceFilterActions, PriceModifiers } from '../src/components/PriceModifiers';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
const filter: SelectableFilter = {
  key: 'life',
  label: '+80 to maximum Life',
  value: 80,
  min: 80,
  max: null,
  selected: true,
  statId: 'life',
  kind: 'stat',
  weight: 1,
};

describe('Mac price filter controls', () => {
  let tree: ReactTestRenderer;
  afterEach(() => {
    if (tree) act(() => tree.unmount());
  });

  it('edits the minimum sent to Search and reports invalid drafts', () => {
    const change = jest.fn(),
      validity = jest.fn();
    act(() => {
      tree = renderer.create(
        <PriceModifiers
          filters={[filter]}
          onChange={change}
          combined={false}
          onValidityChange={validity}
        />,
      );
    });
    const input = tree.root.findByType(TextInput);
    act(() => input.props.onChangeText('72'));
    expect(change).toHaveBeenLastCalledWith([{ ...filter, min: 72 }]);
    act(() => input.props.onChangeText(''));
    expect(validity).toHaveBeenLastCalledWith(false);
    expect(change).toHaveBeenCalledTimes(1);
  });

  it('opens a compact preset menu and applies −10% from the original roll', () => {
    const change = jest.fn();
    act(() => {
      tree = renderer.create(
        <PriceFilterActions
          filters={[filter]}
          onChange={change}
          combined={false}
          onCombinedChange={jest.fn()}
        />,
      );
    });
    act(() =>
      tree.root
        .findByProps({ accessibilityLabel: 'Search minimum preset' })
        .props.onPress(),
    );
    act(() =>
      tree.root
        .findByProps({ accessibilityLabel: 'Apply −10% minimum preset' })
        .props.onPress(),
    );
    expect(change).toHaveBeenLastCalledWith([{ ...filter, min: 72 }]);
    expect(
      tree.root.findByProps({ accessibilityLabel: 'Search minimum preset' }).props
        .accessibilityState.expanded,
    ).toBe(false);
  });
});
