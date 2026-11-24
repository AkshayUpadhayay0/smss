import { FormControl } from '@angular/forms';
import { normaliseWhatsApp, whatsAppValidator } from './whatsapp';

describe('whatsapp', () => {
  it('gives a 10-digit Indian mobile number the +91 code', () => {
    expect(normaliseWhatsApp('9876543210')).toBe('+919876543210');
  });
  it('keeps a number that already has a country code, without spaces', () => {
    expect(normaliseWhatsApp('+91 98765 43210')).toBe('+919876543210');
  });
  it('refuses junk and short numbers', () => {
    expect(normaliseWhatsApp('abc')).toBeNull();
    expect(normaliseWhatsApp('12345')).toBeNull();
  });
  it('the validator passes a good number and fails a bad or blank one', () => {
    expect(whatsAppValidator(new FormControl('9876543210'))).toBeNull();
    expect(whatsAppValidator(new FormControl('12'))).not.toBeNull();
    expect(whatsAppValidator(new FormControl(''))).not.toBeNull();
  });
});
