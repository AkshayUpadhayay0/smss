import { createProfileForm, toProfileRequest } from './profile-form';

describe('school profile WhatsApp', () => {
  it('has no latitude or longitude: a School Admin can never send them', () => {
    const f = createProfileForm();
    expect(Object.keys(f.controls)).not.toContain('latitude');
    expect(Object.keys(f.controls)).not.toContain('longitude');
    const r = toProfileRequest(f) as unknown as Record<string, unknown>;
    expect('latitude' in r).toBe(false);
    expect('longitude' in r).toBe(false);
  });
  it('the WhatsApp number is mandatory unless the same-as-mobile tick is set', () => {
    const f = createProfileForm();
    expect(f.controls.whatsAppNumber.valid).toBe(false);
    f.controls.whatsAppNumber.setValue('9876543210');
    expect(f.controls.whatsAppNumber.valid).toBe(true);
    f.patchValue({ whatsAppSameAsMobile: true });
    expect(toProfileRequest(f).whatsAppNumber).toBeNull();
    expect(toProfileRequest(f).whatsAppSameAsMobile).toBe(true);
  });
});
