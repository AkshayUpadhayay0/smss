/**
 * SINGLE SOURCE OF TRUTH for who may see / enter each route. Both the route guard (roleGuard) and the sidebar read
 * this and nothing else — never repeat a role list anywhere.
 *
 * Role strings are the role NAMES the API returns in `user.roles` and in the JWT role claim (not the role codes).
 * Names are editable in Master Data → Role, so renaming a role there changes who can enter these routes. Keep the
 * names here in step with the API (and see the note in CLAUDE.md about moving the API to role codes).
 *
 * FAIL CLOSED: a route with no entry here is blocked for everyone. Add new routes below.
 */
export const ROLES = {
  SUPER_ADMIN: 'Super Admin',
  SCHOOL_ADMIN: 'School Admin',
  PRINCIPAL: 'Principal',
  TEACHER: 'Teacher',
  ACCOUNTANT: 'Accountant',
  STUDENT: 'Student',
  GUARDIAN: 'Guardian',
} as const;

/** Any signed-in user, whatever their roles (also covers custom roles created in Master Data). */
export const AUTHENTICATED = 'authenticated' as const;

export type RouteAccess = readonly string[] | typeof AUTHENTICATED;

/**
 * Keys are absolute route patterns; `:param` matches any single segment.
 * "/dashboard" must stay open to every signed-in user: it is where denied users are sent, so restricting it
 * would create a redirect loop.
 */
export const ROUTE_ROLES: Readonly<Record<string, RouteAccess>> = {
  '/dashboard': AUTHENTICATED,
  '/change-password': AUTHENTICATED,

  // School-side page: the API resolves the school from the token (400 for accounts without one, e.g. Super Admin).
  '/school-profile': [ROLES.SCHOOL_ADMIN],
  '/students': [ROLES.SCHOOL_ADMIN],
  '/students/onboard-existing': [ROLES.SCHOOL_ADMIN],
  // The Accountant opens a student to collect / void payments; the page shows them the Fees tab only (no Profile tab)
  '/students/:studentId': [ROLES.SCHOOL_ADMIN, ROLES.ACCOUNTANT],
  '/admissions': [ROLES.SCHOOL_ADMIN],
  '/admissions/new': [ROLES.SCHOOL_ADMIN],
  '/admissions/:admissionId': [ROLES.SCHOOL_ADMIN],
  '/school-setup/import-masters': [ROLES.SCHOOL_ADMIN],
  '/school-setup/academic-year': [ROLES.SCHOOL_ADMIN],
  '/school-setup/classes': [ROLES.SCHOOL_ADMIN],
  '/school-setup/sections': [ROLES.SCHOOL_ADMIN],
  '/school-setup/subjects': [ROLES.SCHOOL_ADMIN],
  '/school-setup/document-types': [ROLES.SCHOOL_ADMIN],
  '/school-setup/class-subjects': [ROLES.SCHOOL_ADMIN],
  '/school-setup/employee-designations': [ROLES.SCHOOL_ADMIN],
  '/school-setup/employee-departments': [ROLES.SCHOOL_ADMIN],
  '/school-setup/admission-types': [ROLES.SCHOOL_ADMIN],
  '/fee-setup/fee-types': [ROLES.SCHOOL_ADMIN],
  '/fee-setup/fee-structures': [ROLES.SCHOOL_ADMIN],
  '/fee-setup/fee-concessions': [ROLES.SCHOOL_ADMIN],
  '/fee-setup/fee-structures/new': [ROLES.SCHOOL_ADMIN],
  '/fee-setup/fee-structures/:feeStructureId/edit': [ROLES.SCHOOL_ADMIN],
  // Calendar: every role of a school reads it (the page only shows Add / Edit / Delete to School Admin and Principal; the API re-checks)
  '/calendar': [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL, ROLES.TEACHER, ROLES.ACCOUNTANT, ROLES.STUDENT, ROLES.GUARDIAN],
  // Attendance: the class teacher marks it (the API re-checks they are THAT section's teacher); a School Admin may also
  '/attendance/mark': [ROLES.TEACHER, ROLES.SCHOOL_ADMIN],
  // Timetable: the School Admin builds it (bell schedule + weekly grid per section); a Teacher reads their own
  '/school-setup/periods': [ROLES.SCHOOL_ADMIN],
  '/timetable': [ROLES.SCHOOL_ADMIN],
  '/timetable/my': [ROLES.TEACHER],
  // Employees (staff records, documents, creating logins): School Admin only
  '/employees': [ROLES.SCHOOL_ADMIN],
  '/employees/new': [ROLES.SCHOOL_ADMIN],
  '/employees/:employeeId': [ROLES.SCHOOL_ADMIN],
  '/employees/:employeeId/edit': [ROLES.SCHOOL_ADMIN],
  // Fee dashboard + reports: read-only for the Principal (the API sends canManage=false; the screens hide drill-down / export)
  '/fee-dashboard': [ROLES.SCHOOL_ADMIN, ROLES.ACCOUNTANT, ROLES.PRINCIPAL],
  '/fee-reports/class': [ROLES.SCHOOL_ADMIN, ROLES.ACCOUNTANT, ROLES.PRINCIPAL],

  '/schools': [ROLES.SUPER_ADMIN],
  '/schools/add': [ROLES.SUPER_ADMIN],
  '/schools/:schoolId/edit': [ROLES.SUPER_ADMIN],
  '/schools/:schoolId/view': [ROLES.SUPER_ADMIN],

  '/masters/board-type': [ROLES.SUPER_ADMIN],
  '/masters/school-type': [ROLES.SUPER_ADMIN],
  '/masters/school-level': [ROLES.SUPER_ADMIN],
  '/masters/status': [ROLES.SUPER_ADMIN],
  '/masters/role': [ROLES.SUPER_ADMIN],
  '/masters/religion-category': [ROLES.SUPER_ADMIN],
  '/masters/blood-group': [ROLES.SUPER_ADMIN],
  '/masters/gender': [ROLES.SUPER_ADMIN],
  '/masters/student-category': [ROLES.SUPER_ADMIN],

  // Throwaway page; delete together with src/app/dev.
  '/dev/components-preview': [ROLES.SUPER_ADMIN],
  // Add school-side routes (profile, …) here as they are built.
};

const clean = (url: string): string => {
  const path = url.split(/[?#]/)[0].replace(/\/+$/, '');
  return path === '' ? '/' : path;
};

/** The ROUTE_ROLES entry for a concrete URL (params resolved), or null when the route is not listed. */
export function accessFor(url: string): RouteAccess | null {
  const segments = clean(url).split('/');
  for (const [pattern, access] of Object.entries(ROUTE_ROLES)) {
    const parts = pattern.split('/');
    if (parts.length === segments.length && parts.every((p, i) => p.startsWith(':') || p === segments[i])) return access;
  }
  return null;
}

const norm = (role: string) => role.trim().toLowerCase();

/** True when a user holding `userRoles` may open `url`. Unlisted routes are denied. */
export function canAccess(url: string, userRoles: readonly string[]): boolean {
  const access = accessFor(url);
  if (access === null) return false; // fail closed
  if (access === AUTHENTICATED) return true;
  const have = new Set(userRoles.map(norm));
  return access.some((r) => have.has(norm(r)));
}
