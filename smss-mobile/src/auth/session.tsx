import { router } from 'expo-router';
import { useQueryClient } from '@tanstack/react-query';
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import * as authApi from '../api/auth';
import { api } from '../api';
import { clearAttachmentCache } from '../lib/attachments';
import type { AuthUser } from '../api/types';
import { resolveRole, type AppRole } from './roles';

export type SessionStatus = 'loading' | 'signedOut' | 'signedIn';

interface SessionValue {
  status: SessionStatus;
  user: AuthUser | null;
  /** STUDENT / TEACHER / UNSUPPORTED while signed in; null otherwise. */
  role: AppRole | null;
  /** The account is still on its temporary password: the only screen allowed is Change Password. */
  needsPasswordChange: boolean;
  signIn(loginId: string, password: string): Promise<void>;
  changePassword(currentPassword: string, newPassword: string): Promise<void>;
  signOut(): Promise<void>;
}

const SessionContext = createContext<SessionValue | null>(null);

const LOGOUT_WAIT_MS = 5_000;
const END_HOOK_WAIT_MS = 3_000;

// Features that hold state outside the session (the driver app's location service) register a hook here: it runs when the session ENDS BY ITSELF (the refresh token
// was refused or revoked) so nothing keeps working for a user who is no longer signed in. An explicit sign-out has its own steps. Hooks must not use the login.
type SessionEndHook = () => Promise<void>;
const endHooks = new Set<SessionEndHook>();
export function onSessionEnded(hook: SessionEndHook): () => void {
  endHooks.add(hook);
  return () => void endHooks.delete(hook);
}

export function SessionProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<SessionStatus>('loading');
  const [user, setUser] = useState<AuthUser | null>(null);

  const endLocally = useCallback(() => {
    // Leave the signed-in screens FIRST. Clearing the query cache while their useQuery observers are still mounted makes them resubscribe and
    // refetch in a loop ("Maximum update depth exceeded", found on the emulator when signing out from a data screen). So: stop the queries,
    // flip the state (the route guards unmount the screens), and only then drop the cached data / downloaded files.
    void queryClient.cancelQueries();
    setUser(null);
    setStatus('signedOut');
    setTimeout(() => {
      queryClient.clear(); // never let the next user see the previous user's cached data
      clearAttachmentCache(); // ...or the previous user's downloaded files
    }, 300);
  }, [queryClient]);

  // The session ended on its own (the refresh token was refused or revoked, e.g. the school deactivated this account). Leave through the neutral
  // /signing-out screen exactly like an explicit sign-out: removing the signed-in route group while one of its screens is showing made expo-router loop
  // ("Maximum update depth exceeded", found on the emulator when a driver's refresh tokens were revoked mid-session).
  const expireSession = useCallback(() => {
    void Promise.race([Promise.allSettled([...endHooks].map((h) => h())), new Promise((resolve) => setTimeout(resolve, END_HOOK_WAIT_MS))]);
    try {
      router.replace('/signing-out');
    } catch {
      // navigation not ready: ending the session below still works
    }
    setTimeout(endLocally, 150);
  }, [endLocally]);

  // Wire the HTTP layer's events into React state, then restore the stored session.
  useEffect(() => {
    api.setHandlers({
      onSessionExpired: expireSession,
      onPasswordChangeRequired: () => setUser((u) => (u ? { ...u, isFirstLogin: true } : u)),
      onSession: (fresh) => setUser(fresh),
    });

    let cancelled = false;
    (async () => {
      const stored = await api.loadSession();
      if (cancelled) return;
      if (!stored) {
        setStatus('signedOut');
        return;
      }
      setUser(stored.user);
      setStatus('signedIn');
      // Re-check who we are (roles / first-login may have changed). A refresh happens inside if the token expired.
      // A network failure keeps the stored session; an expired session is handled by onSessionExpired.
      try {
        const me = await authApi.fetchMe();
        if (!cancelled) setUser(me);
      } catch {
        // offline or transient: keep going with what we have
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [expireSession]);

  const signIn = useCallback(
    async (loginId: string, password: string) => {
      const res = await authApi.login(loginId, password);
      queryClient.clear();
      setUser(res.user);
      setStatus('signedIn');
    },
    [queryClient],
  );

  const changePassword = useCallback(async (currentPassword: string, newPassword: string) => {
    const res = await authApi.changePassword(currentPassword, newPassword);
    setUser(res.user); // isFirstLogin is now false: the route guards open up
  }, []);

  const signOut = useCallback(async () => {
    // Move to the neutral screen first (see app/signing-out.tsx), give the navigator a moment, then end the session.
    try {
      router.replace('/signing-out');
    } catch {
      // navigation not ready: ending the session below still works
    }
    await new Promise((resolve) => setTimeout(resolve, 150));
    try {
      const refreshToken = await api.refreshToken();
      if (refreshToken) {
        await Promise.race([authApi.logout(refreshToken), new Promise((resolve) => setTimeout(resolve, LOGOUT_WAIT_MS))]);
      }
    } catch {
      // the server call is best-effort: storage is cleared regardless
    }
    await api.clearSession();
    endLocally();
  }, [endLocally]);

  const value = useMemo<SessionValue>(
    () => ({
      status,
      user,
      role: status === 'signedIn' && user ? resolveRole(user.roles) : null,
      needsPasswordChange: status === 'signedIn' && !!user?.isFirstLogin,
      signIn,
      changePassword,
      signOut,
    }),
    [status, user, signIn, changePassword, signOut],
  );

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession(): SessionValue {
  const ctx = useContext(SessionContext);
  if (!ctx) throw new Error('useSession must be used inside <SessionProvider>');
  return ctx;
}
