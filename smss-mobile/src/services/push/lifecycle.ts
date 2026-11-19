import { resolveRole } from '../../auth/roles';
import type { PushService } from './pushCore';

interface PushUser {
  roles: readonly string[];
  isFirstLogin: boolean;
}

/**
 * When the session code talks to the push service. Push is for the Student and Teacher apps only. Every method resolves, never rejects.
 * A first-login account is skipped: the server answers 403 to everything but change-password until the temporary password is replaced,
 * so registration happens right after that change instead.
 */
export function createPushLifecycle(push: PushService) {
  const wanted = (user: PushUser | null | undefined, ignoreFirstLogin = false) => {
    if (!user || (!ignoreFirstLogin && user.isFirstLogin)) return false;
    const role = resolveRole(user.roles);
    return role === 'STUDENT' || role === 'TEACHER';
  };
  const safe = async (fn: () => Promise<void>) => {
    try {
      await fn();
    } catch {
      // never block the session flow
    }
  };
  return {
    /** After a successful login. */
    afterSignIn: (user: PushUser) => (wanted(user) ? safe(() => push.registerDevice()) : Promise.resolve()),
    /** After the first-login password change succeeded (the user is now fully signed in). */
    afterPasswordChange: (user: PushUser) => (wanted(user, true) ? safe(() => push.registerDevice()) : Promise.resolve()),
    /** After a stored session was restored at app start: the silent retry for earlier failures. */
    afterRestore: (user: PushUser) => (wanted(user) ? safe(() => push.registerDevice()) : Promise.resolve()),
    /** Before sign-out ends the session. */
    beforeSignOut: () => safe(() => push.unregisterDevice()),
  };
}

export type PushLifecycle = ReturnType<typeof createPushLifecycle>;
