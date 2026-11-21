import { ROLES } from './route-roles';

/**
 * Screens that stay closed until the school's setup is complete (academic year, classes, sections, fee types, fee structures):
 * everything that adds students, employees or transport records. The sidebar disables these entries and the setup guard redirects
 * to the next setup step. The API enforces the same rule on the create endpoints (409 SETUP_INCOMPLETE); this list is only the friendly side.
 * One list, read by both the sidebar and the guard: never repeat it.
 */
export const SETUP_GATED_PREFIXES: readonly string[] = [
  '/students',
  '/admissions',
  '/employees',
  '/transport/areas',
  '/transport/vehicles',
  '/transport/drivers',
];

/** Roles that may read the setup status (the same three the API allows). */
export const SETUP_STATUS_ROLES: readonly string[] = [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL, ROLES.ACCOUNTANT];

/** Pages that show the setup progress card / banner above their content. */
export const SETUP_PROGRESS_PREFIXES: readonly string[] = ['/dashboard', '/school-setup', '/fee-setup'];

function under(url: string, prefix: string): boolean {
  const path = url.split(/[?#]/)[0];
  return path === prefix || path.startsWith(prefix + '/');
}

export function isSetupGated(url: string): boolean {
  return SETUP_GATED_PREFIXES.some((p) => under(url, p));
}

export function showsSetupProgress(url: string): boolean {
  return SETUP_PROGRESS_PREFIXES.some((p) => under(url, p));
}
