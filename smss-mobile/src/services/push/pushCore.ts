// Push registration logic with every native/network dependency injected, so it is unit-testable.
// Rules: nothing here may ever throw or reject to the caller (a push problem must never block sign-in), and nothing logs a token.

export type PushOs = 'ANDROID' | 'IOS';

export interface PushMessage {
  title: string | null;
  body: string | null;
}

export interface PushPlatform {
  os: PushOs;
  deviceLabel?: string;
  appVersion?: string;
  /** Asks for the notification permission (Android 13+ prompt / iOS prompt). true = notifications may be shown. */
  requestPermission(): Promise<boolean>;
  /** The FCM token, or null when there is none. */
  getToken(): Promise<string | null>;
  onTokenRefresh(listener: (token: string) => void): () => void;
  onForegroundMessage(listener: (message: PushMessage) => void): () => void;
}

export interface PushApi {
  register(body: { fcmToken: string; platform: PushOs; deviceLabel?: string; appVersion?: string }): Promise<void>;
  unregister(fcmToken: string): Promise<void>;
}

export interface PushDeps {
  platform: PushPlatform;
  api: PushApi;
  /** Shows an in-app banner for a message that arrived while the app is open. */
  onForeground: (message: PushMessage) => void;
  /** Longest time sign-out waits for the unregister call. */
  unregisterTimeoutMs?: number;
}

const UNREGISTER_TIMEOUT_MS = 4_000;

function withTimeout<T>(p: Promise<T>, ms: number): Promise<T | undefined> {
  return Promise.race([p, new Promise<undefined>((resolve) => setTimeout(resolve, ms))]);
}

export function createPushService(deps: PushDeps) {
  const { platform, api } = deps;
  let generation = 0; // bumped on unregister, so a registration that finishes after sign-out does nothing
  let token: string | null = null;
  let stopRefresh: (() => void) | null = null;
  let stopForeground: (() => void) | null = null;
  let inflight: Promise<void> | null = null;

  function body(fcmToken: string) {
    return { fcmToken, platform: platform.os, deviceLabel: platform.deviceLabel, appVersion: platform.appVersion };
  }

  function listen(gen: number) {
    if (!stopRefresh) {
      stopRefresh = platform.onTokenRefresh((fresh) => {
        if (gen !== generation || !fresh) return;
        token = fresh;
        void api.register(body(fresh)).catch(() => {}); // a failed re-registration is retried at the next app start
      });
    }
    if (!stopForeground) {
      stopForeground = platform.onForegroundMessage((m) => {
        try {
          deps.onForeground(m);
        } catch {
          // a banner problem must not surface
        }
      });
    }
  }

  function stopListening() {
    for (const stop of [stopRefresh, stopForeground]) {
      try {
        stop?.();
      } catch {
        // ignore
      }
    }
    stopRefresh = null;
    stopForeground = null;
  }

  async function run(): Promise<void> {
    const gen = generation;
    try {
      listen(gen);
      if (!(await platform.requestPermission())) return;
      const fcmToken = await platform.getToken();
      if (!fcmToken || gen !== generation) return;
      await api.register(body(fcmToken));
      if (gen === generation) token = fcmToken;
    } catch {
      // denied, no token, offline, 4xx/5xx: all silent. The next app start tries again.
    }
  }

  return {
    /** Permission, token, register with the API, and keep it registered. Never throws. Concurrent calls share one run. */
    registerDevice(): Promise<void> {
      if (!inflight) {
        inflight = run().finally(() => {
          inflight = null;
        });
      }
      return inflight;
    },

    /** Call BEFORE the session ends (the call needs the access token). Never throws; waits at most a few seconds. */
    async unregisterDevice(): Promise<void> {
      generation++;
      stopListening();
      const known = token;
      token = null;
      try {
        const ms = deps.unregisterTimeoutMs ?? UNREGISTER_TIMEOUT_MS;
        const fcmToken = known ?? (await withTimeout(platform.getToken(), ms)) ?? null;
        if (fcmToken) await withTimeout(api.unregister(fcmToken), ms);
      } catch {
        // best effort: sign-out continues regardless
      }
    },
  };
}

export type PushService = ReturnType<typeof createPushService>;
