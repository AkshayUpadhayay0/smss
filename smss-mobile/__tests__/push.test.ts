import { createPushLifecycle } from '../src/services/push/lifecycle';
import { createPushService, type PushApi, type PushMessage, type PushPlatform } from '../src/services/push/pushCore';

const student = { roles: ['Student'], isFirstLogin: false };
const firstLoginStudent = { roles: ['Student'], isFirstLogin: true };

function setup(over: { permission?: boolean | 'throw'; token?: string | null | 'throw'; registerFails?: boolean; unregisterFails?: boolean } = {}) {
  const calls = { register: [] as unknown[], unregister: [] as string[], order: [] as string[] };
  let refresh: ((t: string) => void) | null = null;
  let foreground: ((m: PushMessage) => void) | null = null;
  const platform: PushPlatform = {
    os: 'ANDROID',
    deviceLabel: 'Android 14',
    appVersion: '0.1.0',
    requestPermission: async () => {
      if (over.permission === 'throw') throw new Error('boom');
      return over.permission ?? true;
    },
    getToken: async () => {
      if (over.token === 'throw') throw new Error('boom');
      return over.token === undefined ? 'tok-1' : over.token;
    },
    onTokenRefresh: (l) => {
      refresh = l;
      return () => { refresh = null; };
    },
    onForegroundMessage: (l) => {
      foreground = l;
      return () => { foreground = null; };
    },
  };
  const api: PushApi = {
    register: async (b) => {
      calls.register.push(b);
      calls.order.push('register');
      if (over.registerFails) throw new Error('500');
    },
    unregister: async (t) => {
      calls.unregister.push(t);
      calls.order.push('unregister');
      if (over.unregisterFails) throw new Error('500');
    },
  };
  const shown: PushMessage[] = [];
  const service = createPushService({ platform, api, onForeground: (m) => shown.push(m), unregisterTimeoutMs: 50 });
  const lifecycle = createPushLifecycle(service);
  return { calls, shown, lifecycle, service, refresh: (t: string) => refresh?.(t), foreground: (m: PushMessage) => foreground?.(m), hasRefresh: () => refresh !== null };
}

const flush = () => new Promise((r) => setTimeout(r, 0));

describe('push registration', () => {
  it('registers the token after sign-in, with the platform and app details', async () => {
    const t = setup();
    await t.lifecycle.afterSignIn(student);
    expect(t.calls.register).toEqual([{ fcmToken: 'tok-1', platform: 'ANDROID', deviceLabel: 'Android 14', appVersion: '0.1.0' }]);
  });

  it('skips a first-login sign-in (the server would answer 403) and registers after the password change', async () => {
    const t = setup();
    await t.lifecycle.afterSignIn(firstLoginStudent);
    expect(t.calls.register).toHaveLength(0);
    await t.lifecycle.afterPasswordChange({ roles: ['Student'], isFirstLogin: false });
    expect(t.calls.register).toHaveLength(1);
  });

  it('does nothing for roles that are not Student or Teacher', async () => {
    const t = setup();
    await t.lifecycle.afterSignIn({ roles: ['Driver'], isFirstLogin: false });
    await t.lifecycle.afterSignIn({ roles: ['School Admin'], isFirstLogin: false });
    expect(t.calls.register).toHaveLength(0);
  });

  it('registers a teacher too, and again at app start (restore)', async () => {
    const t = setup();
    await t.lifecycle.afterRestore({ roles: ['Teacher'], isFirstLogin: false });
    expect(t.calls.register).toHaveLength(1);
  });

  it('re-registers when the token refreshes', async () => {
    const t = setup();
    await t.lifecycle.afterSignIn(student);
    t.refresh('tok-2');
    await flush();
    expect(t.calls.register).toHaveLength(2);
    expect(t.calls.register[1]).toMatchObject({ fcmToken: 'tok-2' });
  });

  it('a failed re-registration on refresh is swallowed', async () => {
    const t = setup({ registerFails: true });
    await t.lifecycle.afterSignIn(student);
    expect(() => t.refresh('tok-2')).not.toThrow();
    await flush();
  });

  it('unregisters the known token before logout, and stops listening', async () => {
    const t = setup();
    await t.lifecycle.afterSignIn(student);
    await t.lifecycle.beforeSignOut();
    expect(t.calls.unregister).toEqual(['tok-1']);
    expect(t.hasRefresh()).toBe(false);
    t.refresh('tok-3'); // a refresh after sign-out must not register
    await flush();
    expect(t.calls.register).toHaveLength(1);
  });

  it('unregisters the refreshed token, not the stale one', async () => {
    const t = setup();
    await t.lifecycle.afterSignIn(student);
    t.refresh('tok-2');
    await flush();
    await t.lifecycle.beforeSignOut();
    expect(t.calls.unregister).toEqual(['tok-2']);
  });

  it('shows foreground messages through the banner callback', async () => {
    const t = setup();
    await t.lifecycle.afterSignIn(student);
    t.foreground({ title: 'Fee due', body: 'Please pay' });
    expect(t.shown).toEqual([{ title: 'Fee due', body: 'Please pay' }]);
  });
});

describe('push failures never block the session flow', () => {
  it('denied permission: resolves, no token fetched, nothing registered', async () => {
    const t = setup({ permission: false });
    await expect(t.lifecycle.afterSignIn(student)).resolves.toBeUndefined();
    expect(t.calls.register).toHaveLength(0);
  });

  it('permission request throws: resolves', async () => {
    const t = setup({ permission: 'throw' });
    await expect(t.lifecycle.afterSignIn(student)).resolves.toBeUndefined();
    await expect(t.lifecycle.afterPasswordChange(student)).resolves.toBeUndefined();
  });

  it('no token: resolves, nothing registered', async () => {
    const t = setup({ token: null });
    await expect(t.lifecycle.afterSignIn(student)).resolves.toBeUndefined();
    expect(t.calls.register).toHaveLength(0);
  });

  it('getToken throws: resolves', async () => {
    const t = setup({ token: 'throw' });
    await expect(t.lifecycle.afterSignIn(student)).resolves.toBeUndefined();
  });

  it('failed register API call: resolves, and the next start retries', async () => {
    const t = setup({ registerFails: true });
    await expect(t.lifecycle.afterSignIn(student)).resolves.toBeUndefined();
    await expect(t.lifecycle.afterRestore(student)).resolves.toBeUndefined();
    expect(t.calls.register).toHaveLength(2);
  });

  it('failed unregister: sign-out still proceeds', async () => {
    const t = setup({ unregisterFails: true });
    await t.lifecycle.afterSignIn(student);
    await expect(t.lifecycle.beforeSignOut()).resolves.toBeUndefined();
  });

  it('a hanging unregister is cut off by the timeout', async () => {
    const t = setup();
    await t.lifecycle.afterSignIn(student);
    const hang = createPushService({
      platform: { ...({} as PushPlatform), os: 'ANDROID', requestPermission: async () => true, getToken: async () => 'x', onTokenRefresh: () => () => {}, onForegroundMessage: () => () => {} },
      api: { register: async () => {}, unregister: () => new Promise(() => {}) },
      onForeground: () => {},
      unregisterTimeoutMs: 20,
    });
    await hang.registerDevice();
    await expect(hang.unregisterDevice()).resolves.toBeUndefined();
  });

  it('a registration that finishes after sign-out does not register', async () => {
    const t = setup();
    const p = t.lifecycle.afterSignIn(student);
    await t.lifecycle.beforeSignOut();
    await p;
    expect(t.calls.register).toHaveLength(0);
  });

  it('concurrent registrations share one run', async () => {
    const t = setup();
    await Promise.all([t.lifecycle.afterSignIn(student), t.lifecycle.afterRestore(student)]);
    expect(t.calls.register).toHaveLength(1);
  });
});
