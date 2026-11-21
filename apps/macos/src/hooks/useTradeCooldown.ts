import { useCallback, useEffect, useState } from 'react';

/** An API cooldown is a deadline, not a permanently frozen error string. */
export function useTradeCooldown() {
  const [until, setUntil] = useState(0);
  const [now, setNow] = useState(Date.now);
  const seconds = Math.max(0, Math.ceil((until - now) / 1000));
  useEffect(() => {
    if (until <= Date.now()) return;
    const timer = setInterval(() => {
      const time = Date.now();
      setNow(time);
      if (time >= until) clearInterval(timer);
    }, 1000);
    return () => clearInterval(timer);
  }, [until]);
  const start = useCallback((cause: unknown) => {
    const error = cause as { code?: string; retryAfterMs?: number } | null;
    if (
      error?.code !== 'rate_limited' ||
      !Number.isFinite(error.retryAfterMs) ||
      !(error.retryAfterMs! > 0)
    )
      return false;
    const time = Date.now();
    setNow(time);
    setUntil((current) => Math.max(current, time + error.retryAfterMs!));
    return true;
  }, []);
  return {
    seconds,
    start,
    message:
      until === 0
        ? null
        : seconds > 0
          ? `Trade API cooldown · ${seconds}s remaining. Requests are paused.`
          : 'Cooldown finished. Press Search or use the price-check shortcut again.',
  };
}
