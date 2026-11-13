import { resolveApiBaseUrl } from '../src/config/env';

describe('resolveApiBaseUrl', () => {
  it('requires a value', () => {
    expect(() => resolveApiBaseUrl('', true)).toThrow(/not set/);
    expect(() => resolveApiBaseUrl(undefined, true)).toThrow(/not set/);
  });
  it('strips trailing slashes', () => {
    expect(resolveApiBaseUrl('https://x.example.com//', false)).toBe('https://x.example.com');
  });
  it('allows http only in development', () => {
    expect(resolveApiBaseUrl('http://10.0.2.2:5263', true)).toBe('http://10.0.2.2:5263');
    expect(() => resolveApiBaseUrl('http://10.0.2.2:5263', false)).toThrow(/https/);
  });
  it('rejects garbage', () => {
    expect(() => resolveApiBaseUrl('not a url', true)).toThrow(/valid URL/);
  });
});

import { parseDutyIntervalOverride } from '../src/config/env';

describe('parseDutyIntervalOverride (dev builds only)', () => {
  it('accepts whole seconds from 1 to 600', () => {
    expect(parseDutyIntervalOverride('15')).toBe(15);
    expect(parseDutyIntervalOverride(' 60 ')).toBe(60);
    expect(parseDutyIntervalOverride(600)).toBe(600);
  });
  it.each(['', '0', '-5', '601', 'abc', '1.5', undefined, null])('ignores %j', (v) => expect(parseDutyIntervalOverride(v)).toBeNull());
});

import { parseDutyAccuracyOverride } from '../src/config/env';

describe('parseDutyAccuracyOverride (dev builds only)', () => {
  it('accepts only "high"', () => {
    expect(parseDutyAccuracyOverride('high')).toBe('high');
    expect(parseDutyAccuracyOverride(' HIGH ')).toBe('high');
  });
  it.each(['', 'balanced', 'low', 'true', undefined, null, 1])('ignores %j', (v) => expect(parseDutyAccuracyOverride(v)).toBeNull());
});
