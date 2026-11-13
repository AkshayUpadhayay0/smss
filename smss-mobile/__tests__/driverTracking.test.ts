import { createDutyStore, type KeyValue } from '../src/features/driver/dutyStore';
import type { DeviceLocation, PingBody, PingHttpResult, StopReason } from '../src/features/driver/ping';
import { runPing, type PingDeps } from '../src/features/driver/trackingCore';
import { DISCLOSURE_TEXT } from '../src/features/driver/copy';

const fix = (lat = 26.85, ts = Date.UTC(2026, 10, 13, 10, 0, 0)): DeviceLocation => ({ coords: { latitude: lat, longitude: 80.95, accuracy: 9, speed: 5, heading: 90 }, timestamp: ts });

function memoryKv(): KeyValue & { data: Map<string, string> } {
  const data = new Map<string, string>();
  return { data, get: async (k) => data.get(k) ?? null, set: async (k, v) => void data.set(k, v), del: async (k) => void data.delete(k) };
}

function harness(answers: PingHttpResult[] | (() => PingHttpResult | Promise<PingHttpResult>)) {
  const kv = memoryKv();
  const store = createDutyStore(kv);
  const sent: { token: string; body: PingBody }[] = [];
  const stops: StopReason[] = [];
  let reads = 0;
  const queue = Array.isArray(answers) ? [...answers] : null;
  const deps: PingDeps = {
    readToken: async () => {
      reads++;
      return store.readToken();
    },
    send: async (token, body) => {
      sent.push({ token, body });
      if (queue) return queue.shift() ?? { kind: 'response', status: 200, body: { data: { accepted: true } } };
      return (answers as () => PingHttpResult)();
    },
    onStop: async (reason) => {
      stops.push(reason);
      await store.clearToken();
      await store.writeStopped(reason);
    },
  };
  return { kv, store, deps, sent, stops, reads: () => reads };
}

const ok: PingHttpResult = { kind: 'response', status: 200, body: { data: { accepted: true } } };

describe('runPing', () => {
  it('reads the token from storage on EVERY ping and sends that one (a resume replaces it)', async () => {
    const h = harness([ok, ok, ok]);
    await h.store.writeToken('token-one');
    await runPing(h.deps, [fix()]);
    await h.store.writeToken('token-two');                       // a resume at duty start issued a new token
    await runPing(h.deps, [fix()]);
    await h.store.writeToken('token-three');
    await runPing(h.deps, [fix()]);
    expect(h.sent.map((s) => s.token)).toEqual(['token-one', 'token-two', 'token-three']);
    expect(h.reads()).toBe(3);
  });

  it('sends the newest fix of a batch, built into the API body', async () => {
    const h = harness([ok]);
    await h.store.writeToken('t');
    await runPing(h.deps, [fix(10, 1000), fix(20, 3000), fix(30, 2000)]);
    expect(h.sent).toHaveLength(1);
    expect(h.sent[0].body.latitude).toBe(20);
    expect(h.sent[0].body.recordedAt).toBe(new Date(3000).toISOString());
  });

  it('does nothing for an empty batch (no read, no request)', async () => {
    const h = harness([ok]);
    expect(await runPing(h.deps, [])).toEqual({ action: 'NONE' });
    expect(h.reads()).toBe(0);
    expect(h.sent).toHaveLength(0);
  });

  it('with no token on the phone it STOPS (and says why) instead of sending', async () => {
    const h = harness([ok]);
    expect(await runPing(h.deps, [fix()])).toEqual({ action: 'STOP', stopReason: 'NO_TOKEN' });
    expect(h.sent).toHaveLength(0);
    expect(h.stops).toEqual(['NO_TOKEN']);
  });

  it.each([
    [401, { code: 'UNKNOWN_TOKEN' }, 'UNKNOWN_TOKEN'],
    [409, { code: 'SESSION_ENDED' }, 'SESSION_ENDED'],
    [409, { code: 'SESSION_EXPIRED' }, 'SESSION_EXPIRED'],
    [409, { code: 'DRIVER_DEACTIVATED' }, 'DRIVER_DEACTIVATED'],
  ])('STOP on %s %j: the token is deleted and the reason remembered', async (status, data, reason) => {
    const h = harness([{ kind: 'response', status, body: { data } }]);
    await h.store.writeToken('secret-token');
    const out = await runPing(h.deps, [fix()]);
    expect(out).toEqual({ action: 'STOP', stopReason: reason });
    expect(await h.store.readToken()).toBeNull();                // deleted
    expect((await h.store.readStopped())?.reason).toBe(reason);  // the UI shows "duty ended" from this
  });

  it.each([
    ['network failure', { kind: 'network' } as PingHttpResult],
    ['429', { kind: 'response', status: 429, body: { data: { code: 'TOO_SOON' } } } as PingHttpResult],
    ['500', { kind: 'response', status: 500, body: null } as PingHttpResult],
    ['503', { kind: 'response', status: 503, body: null } as PingHttpResult],
  ])('RETRY on %s: nothing is stopped or deleted, the token stays for the next ping', async (_label, result) => {
    const h = harness([result, ok]);
    await h.store.writeToken('keep-me');
    expect((await runPing(h.deps, [fix()])).action).toBe('RETRY');
    expect(h.stops).toEqual([]);
    expect(await h.store.readToken()).toBe('keep-me');
    expect((await runPing(h.deps, [fix(26.9)])).action).toBe('OK');   // the next ping sends a fresh fix, nothing was queued
    expect(h.sent).toHaveLength(2);
    expect(h.sent[1].body.latitude).toBe(26.9);
  });

  it.each(['LOW_ACCURACY', 'STALE_FIX', 'OUT_OF_ORDER', 'MOCK_LOCATION'])('DROP on 200 accepted:false %s: duty carries on', async (reason) => {
    const h = harness([{ kind: 'response', status: 200, body: { data: { accepted: false, reason } } }]);
    await h.store.writeToken('t');
    expect(await runPing(h.deps, [fix()])).toEqual({ action: 'DROP', reason });
    expect(h.stops).toEqual([]);
    expect(await h.store.readToken()).toBe('t');
  });

  it('never throws: a failing send is a RETRY, a failing storage read is a RETRY', async () => {
    const h = harness(() => {
      throw new Error('boom');
    });
    await h.store.writeToken('t');
    expect((await runPing(h.deps, [fix()])).action).toBe('RETRY');
    const broken: PingDeps = { ...h.deps, readToken: async () => { throw new Error('keystore locked'); } };
    expect((await runPing(broken, [fix()])).action).toBe('RETRY');
  });

  it('does not log anything (no coordinates, tokens or bodies)', async () => {
    const spies = (['log', 'info', 'warn', 'error', 'debug'] as const).map((m) => jest.spyOn(console, m).mockImplementation(() => {}));
    const h = harness([ok, { kind: 'response', status: 401, body: null }, { kind: 'network' }]);
    await h.store.writeToken('super-secret-token');
    await runPing(h.deps, [fix()]);
    await runPing(h.deps, [fix()]);
    await runPing(h.deps, [fix()]);
    for (const s of spies) {
      expect(s).not.toHaveBeenCalled();
      s.mockRestore();
    }
  });
});

describe('dutyStore', () => {
  it('keeps the token only under one secure-storage key and clears it', async () => {
    const kv = memoryKv();
    const store = createDutyStore(kv);
    await store.writeToken('abc');
    expect([...kv.data.keys()]).toEqual(['smss.duty.token']);
    expect(await store.readToken()).toBe('abc');
    await store.clearToken();
    expect(await store.readToken()).toBeNull();
  });

  it('remembers why sharing stopped and ignores a corrupt record', async () => {
    const kv = memoryKv();
    const store = createDutyStore(kv);
    expect(await store.readStopped()).toBeNull();
    await store.writeStopped('SESSION_EXPIRED', new Date('2026-11-13T10:00:00Z'));
    expect(await store.readStopped()).toEqual({ reason: 'SESSION_EXPIRED', at: '2026-11-13T10:00:00.000Z' });
    kv.data.set('smss.duty.stopped', '{not json');
    expect(await store.readStopped()).toBeNull();
    kv.data.set('smss.duty.stopped', JSON.stringify({ reason: 'MADE_UP', at: 'x' }));
    expect(await store.readStopped()).toBeNull();
    await store.clearStopped();
  });

  it('remembers the disclosure was accepted for this version of the text', async () => {
    const kv = memoryKv();
    const store = createDutyStore(kv);
    expect(await store.disclosureAccepted()).toBe(false);
    await store.acceptDisclosure();
    expect(await store.disclosureAccepted()).toBe(true);
    kv.data.set('smss.duty.disclosure', '0');                    // an older version: shown again
    expect(await store.disclosureAccepted()).toBe(false);
  });
});

describe('the disclosure text', () => {
  it('is exactly the approved wording', () => {
    expect(DISCLOSURE_TEXT).toBe(
      "SMSS shares this vehicle's location with your school, and with students assigned to your area, only while you are on duty. Sharing starts when you tap Mark today's attendance and stops when you tap End duty. SMSS does not keep a location history.",
    );
  });
});
