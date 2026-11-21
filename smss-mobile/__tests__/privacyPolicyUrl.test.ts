import { resolvePrivacyPolicyUrl } from '../src/config/env';

describe('resolvePrivacyPolicyUrl', () => {
  it('accepts an https address', () => expect(resolvePrivacyPolicyUrl(' https://smss-erp.digital/privacy-policy ')).toBe('https://smss-erp.digital/privacy-policy'));
  it.each([undefined, null, '', '   ', 'http://smss-erp.digital/privacy-policy', 'not a url', 'javascript:alert(1)', 42])('ignores %p', (v) => expect(resolvePrivacyPolicyUrl(v)).toBeNull());
});
