import { AbstractControl, ValidationErrors } from '@angular/forms';

const SEPARATORS = /[\s\-().]/g;
const INDIAN_MOBILE = /^[6-9][0-9]{9}$/;

/**
 * The same rule as the API (PhoneNumbers.Normalise): a WhatsApp number is stored WITH its country code. A 10-digit Indian mobile number gets +91; "+91 98765 43210",
 * "919876543210" and "09876543210" are read as the same number; another country's number must start with "+". Returns the stored form, or null when blank or not valid.
 */
export function normaliseWhatsApp(raw: string | null | undefined): string | null {
  const s = (raw ?? '').trim().replace(SEPARATORS, '');
  if (!s) return null;
  if (s.startsWith('+')) {
    const rest = s.slice(1);
    if (!/^[0-9]+$/.test(rest) || rest.length < 8 || rest.length > 15 || rest[0] === '0') return null;
    if (rest.startsWith('91')) return rest.length === 12 && INDIAN_MOBILE.test(rest.slice(2)) ? `+${rest}` : null;
    return `+${rest}`;
  }
  if (!/^[0-9]+$/.test(s)) return null;
  if (s.length === 10 && INDIAN_MOBILE.test(s)) return `+91${s}`;
  if (s.length === 12 && s.startsWith('91') && INDIAN_MOBILE.test(s.slice(2))) return `+${s}`;
  if (s.length === 11 && s[0] === '0' && INDIAN_MOBILE.test(s.slice(1))) return `+91${s.slice(1)}`;
  return null;
}

/** Required and well formed. (A control that is disabled, e.g. by "same as mobile number", is not validated at all.) */
export function whatsAppValidator(control: AbstractControl): ValidationErrors | null {
  const v = String(control.value ?? '').trim();
  if (v === '') return { required: true };
  return normaliseWhatsApp(v) ? null : { pattern: 'Enter a 10-digit mobile number, or a number with its country code, for example +91 98765 43210' };
}
