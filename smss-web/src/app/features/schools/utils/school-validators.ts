import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

// Same patterns as the API (Rx / SchoolCodeRules).
export const MOBILE_RE = /^[6-9][0-9]{9}$/;
export const GSTIN_RE = /^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$/i;
export const PAN_RE = /^[A-Z]{5}[0-9]{4}[A-Z]$/i;
export const PINCODE_RE = /^[1-9][0-9]{5}$/;
export const SCHOOL_CODE_RE = /^[A-Za-z0-9_-]+$/;

/** Custom validators put their message in the error value so the UI can show it directly. */
export function pattern(re: RegExp, message: string): ValidatorFn {
  return (c: AbstractControl): ValidationErrors | null => {
    const v = String(c.value ?? '').trim();
    return v === '' || re.test(v) ? null : { pattern: message };
  };
}

export const mobileValidator = pattern(MOBILE_RE, 'Enter a valid 10-digit mobile number');

/** Mirrors the API's [Url]: http, https or ftp. */
export function urlValidator(c: AbstractControl): ValidationErrors | null {
  const v = String(c.value ?? '').trim();
  return v === '' || /^(https?|ftp):\/\/\S+$/i.test(v) ? null : { url: 'Enter a valid URL starting with http:// or https://' };
}

export function establishYearValidator(c: AbstractControl): ValidationErrors | null {
  const v = String(c.value ?? '').trim();
  if (v === '') return null;
  const year = Number(v);
  if (!Number.isInteger(year) || year < 1800) return { year: 'Enter a valid year (1800 or later)' };
  if (year > new Date().getFullYear()) return { year: 'Establish year cannot be in the future' };
  return null;
}

export function positiveIntValidator(c: AbstractControl): ValidationErrors | null {
  const v = String(c.value ?? '').trim();
  return v === '' || /^[1-9][0-9]*$/.test(v) ? null : { int: 'Enter a whole number' };
}

/** Put on the end-date control; reads its sibling `subscriptionStartDate`. */
export function endDateValidator(c: AbstractControl): ValidationErrors | null {
  const end = String(c.value ?? '');
  const start = String(c.parent?.get('subscriptionStartDate')?.value ?? '');
  return end && start && end < start ? { range: 'End date cannot be before start date' } : null;
}

/** Exactly one primary contact whenever contacts exist. */
export function primaryContactValidator(array: AbstractControl): ValidationErrors | null {
  const rows = (array.value ?? []) as { isPrimary: boolean }[];
  if (rows.length === 0) return null;
  return rows.filter((r) => r.isPrimary).length === 1 ? null : { primary: 'Mark exactly one contact as the primary contact' };
}

/** Human message for the first error on a control; shown once the control was touched. */
export function firstErrorMessage(control: AbstractControl | null, force = false): string | undefined {
  if (!control || control.valid || control.disabled || !(force || control.touched)) return undefined;
  const errors = control.errors ?? {};
  for (const [key, value] of Object.entries(errors)) {
    if (typeof value === 'string') return value;
    switch (key) {
      case 'required':
        return 'This field is required';
      case 'email':
        return 'Enter a valid email address';
      case 'maxlength':
        return `Maximum ${(value as { requiredLength: number }).requiredLength} characters`;
      case 'pattern':
        return 'Invalid format';
    }
  }
  return 'Invalid value';
}
