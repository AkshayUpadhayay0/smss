import { useQuery } from '@tanstack/react-query';
import { useEffect } from 'react';
import { AppState } from 'react-native';
import { getMe } from './dutyApi';
import { isSharing } from './duty';
import { dutyStore } from './nativeStore';
import { readPermissions } from './permissionsNative';

// The driver app's query keys and hooks. The cache is cleared on sign-in / sign-out, so keys need no user id.
export const driverKeys = {
  me: ['driver', 'me'] as const,
  sharing: ['driver', 'sharing'] as const,
  stopped: ['driver', 'stopped'] as const,
  permissions: ['driver', 'permissions'] as const,
  disclosure: ['driver', 'disclosure'] as const,
};

/** The profile, vehicle, areas and duty state. The SERVER's view: refreshed every 30 s while the screen is open, and on pull-to-refresh. */
export const useDriverMe = () => useQuery({ queryKey: driverKeys.me, queryFn: getMe, refetchInterval: 30_000 });

/** Is the location service running on this phone? (Not the same as "on duty": the server owns that.) */
export const useServiceRunning = () => useQuery({ queryKey: driverKeys.sharing, queryFn: isSharing, refetchInterval: 10_000, staleTime: 0 });

/** Why sharing last stopped by itself (401 / 409 from a ping), remembered by the background task. */
export const useStoppedReason = () => useQuery({ queryKey: driverKeys.stopped, queryFn: async () => (await dutyStore.readStopped())?.reason ?? null, refetchInterval: 10_000, staleTime: 0 });

export const useDisclosureAccepted = () => useQuery({ queryKey: driverKeys.disclosure, queryFn: () => dutyStore.disclosureAccepted(), staleTime: Infinity });

/** The three permissions. Re-read whenever the app comes back to the foreground: the driver may have changed them in Settings. */
export function usePermissions() {
  const q = useQuery({ queryKey: driverKeys.permissions, queryFn: readPermissions, staleTime: 0 });
  const { refetch } = q;
  useEffect(() => {
    const sub = AppState.addEventListener('change', (s) => {
      if (s === 'active') void refetch();
    });
    return () => sub.remove();
  }, [refetch]);
  return q;
}
