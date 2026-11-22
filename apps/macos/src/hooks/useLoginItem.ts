import { useCallback, useEffect, useState } from 'react';
import type { LoginItemStatus } from '../native/VLSettings';
import {
  getOpenAtLogin,
  openLoginItemsSettings,
  setOpenAtLogin,
} from '../native/VLSettings';

/**
 * Whether macOS opens ExileWorth at login. The system owns this state (the
 * user can change it in System Settings), so it is read back rather than
 * stored in the app's own settings.
 */
export function useLoginItem() {
  const [status, setStatus] = useState<LoginItemStatus | null>(null);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(() => {
    getOpenAtLogin()
      .then(setStatus)
      .catch(() => setStatus('disabled'));
  }, []);

  useEffect(refresh, [refresh]);

  const set = useCallback(async (enabled: boolean) => {
    setError(null);
    try {
      setStatus(await setOpenAtLogin(enabled));
    } catch (cause: unknown) {
      setError(
        cause instanceof Error ? cause.message : 'macOS did not allow the change.',
      );
    }
  }, []);

  const openSystemSettings = useCallback(() => {
    void openLoginItemsSettings();
  }, []);

  return { status, error, set, refresh, openSystemSettings };
}
