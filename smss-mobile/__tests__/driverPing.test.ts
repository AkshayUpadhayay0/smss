import { buildPingBody, classifyPing, newestLocation, type DeviceLocation } from '../src/features/driver/ping';

const loc = (over: Partial<DeviceLocation['coords']> = {}, extra: Partial<DeviceLocation> = {}): DeviceLocation => ({
  coords: { latitude: 26.8467123456, longitude: 80.9462, accuracy: 12.5, speed: 7.2, heading: 91, ...over },
  timestamp: Date.UTC(2026, 10, 13, 10, 0, 0),
  ...extra,
});

describe('buildPingBody', () => {
  it('maps a fix to the API body (names, rounding, ISO time)', () => {
    expect(buildPingBody(loc())).toEqual({
      latitude: 26.846712, longitude: 80.9462, accuracyMeters: 12.5, speedMps: 7.2, headingDegrees: 91, recordedAt: '2026-11-13T10:00:00.000Z', mocked: false,
    });
  });
  it('sends null, not -1, for values the device does not know (the server answers 400 to negatives)', () => {
    const b = buildPingBody(loc({ accuracy: -1, speed: -1, heading: -1 }));
    expect([b.accuracyMeters, b.speedMps, b.headingDegrees]).toEqual([null, null, null]);
    const c = buildPingBody(loc({ accuracy: null, speed: undefined, heading: Number.NaN }));
    expect([c.accuracyMeters, c.speedMps, c.headingDegrees]).toEqual([null, null, null]);
  });
  it('keeps a zero speed and a heading of 0 or 360 (valid readings), drops a heading above 360', () => {
    expect(buildPingBody(loc({ speed: 0, heading: 0 })).speedMps).toBe(0);
    expect(buildPingBody(loc({ heading: 0 })).headingDegrees).toBe(0);
    expect(buildPingBody(loc({ heading: 360 })).headingDegrees).toBe(360);
    expect(buildPingBody(loc({ heading: 400 })).headingDegrees).toBeNull();
  });
  it('carries the mock-provider flag, false unless it is exactly true', () => {
    expect(buildPingBody(loc({}, { mocked: true })).mocked).toBe(true);
    expect(buildPingBody(loc({}, { mocked: false })).mocked).toBe(false);
    expect(buildPingBody(loc()).mocked).toBe(false);
  });
  it('has exactly the fields the API reads and nothing else (no token, no ids, no school)', () => {
    expect(Object.keys(buildPingBody(loc())).sort()).toEqual(['accuracyMeters', 'headingDegrees', 'latitude', 'longitude', 'mocked', 'recordedAt', 'speedMps']);
  });
});

describe('newestLocation', () => {
  it('picks the latest fix of a batch and handles an empty one', () => {
    const a = loc({}, { timestamp: 1000 });
    const b = loc({ latitude: 1 }, { timestamp: 3000 });
    const c = loc({ latitude: 2 }, { timestamp: 2000 });
    expect(newestLocation([a, b, c])).toBe(b);
    expect(newestLocation([])).toBeNull();
    expect(newestLocation(null)).toBeNull();
  });
});

describe('classifyPing: the stop / retry / drop table', () => {
  const res = (status: number, data?: unknown) => ({ kind: 'response' as const, status, body: { status: status < 400, statusCode: status, message: 'x', data } });

  it('200 accepted -> OK', () => expect(classifyPing(res(200, { accepted: true }))).toEqual({ action: 'OK' }));

  it.each(['LOW_ACCURACY', 'STALE_FIX', 'FUTURE_FIX', 'OUT_OF_ORDER', 'MOCK_LOCATION'])('200 accepted:false (%s) -> DROP, keep going', (reason) => {
    expect(classifyPing(res(200, { accepted: false, reason }))).toEqual({ action: 'DROP', reason });
  });

  it('401 -> STOP', () => expect(classifyPing(res(401, { code: 'UNKNOWN_TOKEN' }))).toEqual({ action: 'STOP', stopReason: 'UNKNOWN_TOKEN' }));
  it('401 with no readable body is still STOP', () => expect(classifyPing({ kind: 'response', status: 401, body: null })).toEqual({ action: 'STOP', stopReason: 'UNKNOWN_TOKEN' }));

  it.each(['SESSION_ENDED', 'SESSION_EXPIRED', 'DRIVER_DEACTIVATED'])('409 %s -> STOP with that reason', (code) => {
    expect(classifyPing(res(409, { code }))).toEqual({ action: 'STOP', stopReason: code });
  });
  it('an unknown 409 is STOP too (the session cannot continue)', () => expect(classifyPing(res(409, { code: 'SOMETHING_NEW' })).action).toBe('STOP'));

  it('network failure -> RETRY on the next ping', () => expect(classifyPing({ kind: 'network' })).toEqual({ action: 'RETRY' }));
  it('429 -> RETRY on the next ping (not a stop)', () => expect(classifyPing(res(429, { code: 'TOO_SOON', retryAfterSeconds: 15 }))).toEqual({ action: 'RETRY' }));
  it.each([500, 502, 503, 504])('%s -> RETRY', (status) => expect(classifyPing(res(status)).action).toBe('RETRY'));
  it('400 (a bad value) -> DROP: the next fix is a new reading', () => expect(classifyPing(res(400, { code: 'INVALID' })).action).toBe('DROP'));
  it.each([403, 404, 418, 302])('an unexpected %s -> RETRY (never end duty over a gateway oddity)', (status) => expect(classifyPing(res(status)).action).toBe('RETRY'));
});
