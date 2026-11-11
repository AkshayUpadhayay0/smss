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
