import React from 'react';
import renderer, { act, type ReactTestRenderer } from 'react-test-renderer';
import { useTradeCooldown } from '../src/hooks/useTradeCooldown';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('trade cooldown countdown', () => {
  let current: ReturnType<typeof useTradeCooldown>;
  let tree: ReactTestRenderer;
  function Probe() {
    current = useTradeCooldown();
    return null;
  }
  beforeEach(() => {
    jest.useFakeTimers();
    act(() => {
      tree = renderer.create(<Probe />);
    });
  });
  afterEach(() => {
    act(() => tree.unmount());
    jest.useRealTimers();
  });

  it('counts down and explicitly reports when a retry is available', () => {
    act(() => {
      current.start({ code: 'rate_limited', retryAfterMs: 300_000 });
    });
    expect(current.seconds).toBe(300);
    act(() => jest.advanceTimersByTime(299_000));
    expect(current.seconds).toBe(1);
    act(() => jest.advanceTimersByTime(1000));
    expect(current.seconds).toBe(0);
    expect(current.message).toContain('Cooldown finished');
  });

  it('does not restart the wait when given only the remaining delay', () => {
    act(() => {
      current.start({ code: 'rate_limited', retryAfterMs: 300_000 });
    });
    act(() => jest.advanceTimersByTime(100_000));
    act(() => {
      current.start({ code: 'rate_limited', retryAfterMs: 200_000 });
    });
    expect(current.seconds).toBe(200);
    act(() => jest.advanceTimersByTime(200_000));
    expect(current.seconds).toBe(0);
    expect(current.start(new Error('Network failed'))).toBe(false);
  });

  it('expires on the next tick after the computer sleeps past the deadline', () => {
    act(() => {
      current.start({ code: 'rate_limited', retryAfterMs: 300_000 });
    });
    jest.setSystemTime(Date.now() + 3_600_000);
    act(() => jest.advanceTimersByTime(1000));
    expect(current.seconds).toBe(0);
    expect(current.message).toContain('Cooldown finished');
  });
});
