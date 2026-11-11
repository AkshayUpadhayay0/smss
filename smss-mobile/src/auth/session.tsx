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

export function SessionProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<SessionStatus>('loading');
  const [user, setUser] = useState<AuthUser | null>(null);

  const endLocally = useCallback(() => {
    queryClient.clear(); // never let the next user see the previous user's cached data
    clearAttachmentCache(); // ...or the previous user's downloaded files
    setUser(null);
    setStatus('signedOut');
  }, [queryClient]);

  // Wire the HTTP layer's events into React state, then restore the stored session.
  useEffect(() => {
    api.setHandlers({
      onSessionExpired: endLocally,
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
  }, [endLocally]);

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
