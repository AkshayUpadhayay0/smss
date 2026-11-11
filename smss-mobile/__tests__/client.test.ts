import { createApiClient, type SessionStore, type StoredSession } from '../src/api/client';
import type { AuthUser, LoginResponse } from '../src/api/types';

const user: AuthUser = {
  userId: 1, displayName: 'Asha', orgUserId: 'DPS001ST001', userType: 'Student', email: null, schoolId: 'S1', schoolCode: 'C', schoolName: 'School',
  logoUrl: null, roles: ['Student'], isFirstLogin: false,
};
const login = (n: number): LoginResponse => ({ token: `access-${n}`, expiresAt: '', refreshToken: `refresh-${n}`, refreshTokenExpiresAt: '', user });
const envelope = (data: unknown, statusCode = 200) => ({ status: statusCode < 400, statusCode, message: 'ok', data });

function res(status: number, body: unknown): Response {
  return { status, ok: status >= 200 && status < 300, text: async () => (body === undefined ? '' : JSON.stringify(body)) } as Response;
}

function memoryStore(initial: StoredSession | null) {
  let value = initial;
  const writes: StoredSession[] = [];
  const store: SessionStore = {
    read: async () => value,
    write: async (s) => { writes.push(s); value = s; },
    clear: async () => { value = null; },
  };
  return { store, writes, get value() { return value; } };
}

const session = (n: number): StoredSession => ({ accessToken: `access-${n}`, refreshToken: `refresh-${n}`, user });

/** A fake server: the data endpoint accepts only the token in `valid`; /Auth/refresh follows `refreshImpl`. */
function setup(opts: { initial?: StoredSession | null; refreshImpl?: (calls: number, body: { refreshToken: string }) => Promise<Response> | Response } = {}) {
  const mem = memoryStore(opts.initial === undefined ? session(1) : opts.initial);
  const valid = { token: 'access-2' };
  const calls = { refresh: 0, data: 0 };
  const events = { expired: 0, passwordChange: 0 };
  const fetchFn = jest.fn(async (url: RequestInfo | URL, init?: RequestInit) => {
    const u = String(url);
    const headers = (init?.headers ?? {}) as Record<string, string>;
    if (u.endsWith('/api/Auth/refresh')) {
      calls.refresh++;
      const body = JSON.parse(String(init?.body));
      return opts.refreshImpl ? opts.refreshImpl(calls.refresh, body) : res(200, envelope(login(2)));
    }
    calls.data++;
    if (u.includes('/first-login')) return res(403, { status: false, statusCode: 403, message: 'Password change required. Change your temporary password to continue.', data: null });
    return headers.Authorization === `Bearer ${valid.token}` ? res(200, envelope({ n: calls.data })) : res(401, { message: 'x' });
  }) as unknown as typeof fetch;
  const client = createApiClient({ baseUrl: () => 'https://api.test', store: mem.store, fetchFn, timeoutMs: 1000 });
  client.setHandlers({ onSessionExpired: () => { events.expired++; }, onPasswordChangeRequired: () => { events.passwordChange++; } });
  return { client, mem, calls, events, fetchFn };
}

describe('api client refresh', () => {
  it('sends the bearer token and unwraps the envelope', async () => {
    const { client, fetchFn } = setup({ initial: session(2) });
    await expect(client.request('/api/thing')).resolves.toEqual({ n: 1 });
    expect((fetchFn as jest.Mock).mock.calls[0][1].headers.Authorization).toBe('Bearer access-2');
  });

  it('runs exactly ONE refresh for concurrent 401s and retries every request with the new token', async () => {
    const { client, calls, mem } = setup({ refreshImpl: async () => { await new Promise((r) => setTimeout(r, 20)); return res(200, envelope(login(2))); } });
    const results = await Promise.all([client.request('/api/a'), client.request('/api/b'), client.request('/api/c')]);
    expect(results).toHaveLength(3);
    expect(calls.refresh).toBe(1);
    expect(mem.value?.refreshToken).toBe('refresh-2');
  });

  it('writes the new pair to storage BEFORE any waiting request is released', async () => {
    const { client, mem } = setup();
    let writtenWhenReleased: string | undefined;
    const original = mem.store.write;
    mem.store.write = async (s) => { await new Promise((r) => setTimeout(r, 15)); await original(s); };
    await client.request('/api/a');
    writtenWhenReleased = mem.value?.refreshToken;
    expect(writtenWhenReleased).toBe('refresh-2');
  });

  it('retries a refresh ONCE with the same token after a network error', async () => {
    const seen: string[] = [];
    const { client, calls } = setup({
      refreshImpl: (n, body) => {
        seen.push(body.refreshToken);
        if (n === 1) throw new TypeError('Network request failed');
        return res(200, envelope(login(2)));
      },
    });
    await expect(client.request('/api/a')).resolves.toBeDefined();
    expect(calls.refresh).toBe(2);
    expect(seen).toEqual(['refresh-1', 'refresh-1']);
  });

  it('keeps the session when the refresh retry also fails with no response', async () => {
    const { client, calls, mem, events } = setup({ refreshImpl: () => { throw new TypeError('Network request failed'); } });
    await expect(client.request('/api/a')).rejects.toMatchObject({ kind: 'network' });
    expect(calls.refresh).toBe(2); // not three, not parallel
    expect(mem.value).not.toBeNull();
    expect(events.expired).toBe(0);
  });

  it('a 401 from refresh clears the session and signals sign-out', async () => {
    const { client, mem, events } = setup({ refreshImpl: () => res(401, { message: 'Your session has expired.' }) });
    await expect(client.request('/api/a')).rejects.toMatchObject({ kind: 'unauthorized' });
    expect(mem.value).toBeNull();
    expect(events.expired).toBe(1);
  });

  it('a server error from refresh does NOT end the session', async () => {
    const { client, mem, events } = setup({ refreshImpl: () => res(500, { message: 'boom' }) });
    await expect(client.request('/api/a')).rejects.toMatchObject({ kind: 'server' });
    expect(mem.value).not.toBeNull();
    expect(events.expired).toBe(0);
  });

  it('ends the session when the request is still 401 after a successful refresh', async () => {
    const { client, mem, events } = setup({ refreshImpl: () => res(200, envelope(login(3))) }); // access-3 is not accepted by the fake server
    await expect(client.request('/api/a')).rejects.toMatchObject({ kind: 'unauthorized' });
    expect(mem.value).toBeNull();
    expect(events.expired).toBe(1);
  });

  it('does not resurrect a session that was signed out while a refresh was in flight', async () => {
    let release!: () => void;
    const gate = new Promise<void>((r) => { release = r; });
    const { client, mem } = setup({ refreshImpl: async () => { await gate; return res(200, envelope(login(2))); } });
    const pending = client.request('/api/a');
    await new Promise((r) => setTimeout(r, 10));
    await client.clearSession();
    release();
    await expect(pending).rejects.toMatchObject({ kind: 'unauthorized' });
    expect(mem.value).toBeNull();
  });

  it('turns the first-login 403 into password_change_required and notifies, without touching the session', async () => {
    const { client, events, mem } = setup({ initial: session(2) });
    await expect(client.request('/api/first-login')).rejects.toMatchObject({ kind: 'password_change_required' });
    expect(events.passwordChange).toBe(1);
    expect(mem.value).not.toBeNull();
  });

  it('does not try to refresh an anonymous call (a wrong password is just a 401)', async () => {
    const { client, calls } = setup({ initial: null });
    await expect(client.request('/api/Auth/login', { method: 'POST', auth: false, body: {} })).rejects.toMatchObject({ kind: 'unauthorized' });
    expect(calls.refresh).toBe(0);
  });

  it('refuses an authenticated call with no session instead of hitting the network', async () => {
    const { client, fetchFn } = setup({ initial: null });
    await expect(client.request('/api/a')).rejects.toMatchObject({ kind: 'unauthorized' });
    expect(fetchFn).not.toHaveBeenCalled();
  });
});
