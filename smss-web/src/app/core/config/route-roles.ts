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
  ADMISSION_STAFF: 'Admission Staff',
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
  '/students/import': [ROLES.SCHOOL_ADMIN],
  // A Teacher's own students (read-only; the API scopes them to the sections they are class teacher of or teach in)
  '/my-students': [ROLES.TEACHER],
  '/my-students/:studentId': [ROLES.TEACHER],
  // The Accountant opens a student to collect / void payments; the page shows them the Fees tab only (no Profile tab)
  '/students/:studentId': [ROLES.SCHOOL_ADMIN, ROLES.ACCOUNTANT],
  // Admission applications: School Admin, Principal and Admission Staff work them (the API re-checks; only School Admin / Principal see Approve / Reject).
  '/admissions': [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL, ROLES.ADMISSION_STAFF],
  '/admissions/new': [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL, ROLES.ADMISSION_STAFF],
  '/admissions/:admissionId': [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL, ROLES.ADMISSION_STAFF],
  '/admissions/:admissionId/edit': [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL, ROLES.ADMISSION_STAFF],
  '/school-setup/import-masters': [ROLES.SCHOOL_ADMIN],
  '/school-setup/academic-year': [ROLES.SCHOOL_ADMIN],
  '/school-setup/classes': [ROLES.SCHOOL_ADMIN],
  '/school-setup/sections': [ROLES.SCHOOL_ADMIN],
  '/school-setup/subjects': [ROLES.SCHOOL_ADMIN],
  '/school-setup/document-types': [ROLES.SCHOOL_ADMIN],
  // ID card template + branding (School Admin only; the API enforces it too)
  '/school-setup/id-card': [ROLES.SCHOOL_ADMIN],
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
  '/calendar': [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL, ROLES.TEACHER, ROLES.ACCOUNTANT, ROLES.STUDENT, ROLES.GUARDIAN, ROLES.ADMISSION_STAFF],
  // Attendance: the class teacher marks it (the API re-checks they are THAT section's teacher); a School Admin may also
  '/attendance/mark': [ROLES.TEACHER, ROLES.SCHOOL_ADMIN],
  // Monthly attendance report: School Admin and Principal (read-only) for any section; a Teacher gets it for the section they are class teacher of (the API enforces which)
  '/attendance/report': [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL, ROLES.TEACHER],
  // Homework (publish + view): a Teacher sets it for what the Timetable says they teach, a School Admin for any section; a Student reads their own section's.
  // Principal / Accountant / Guardian have no access yet. The API re-checks every action (eligibility, ownership, section).
  '/homework': [ROLES.TEACHER, ROLES.SCHOOL_ADMIN],
  '/homework/my': [ROLES.STUDENT],
  // Assessments (internal class tests): a Teacher records them for what the Timetable says they teach, a School Admin for any section; a Student reads their own marks.
  // Principal / Accountant / Admission Staff / Guardian have no access. The API re-checks every action (eligibility, ownership, section, mapping).
  '/assessments': [ROLES.TEACHER, ROLES.SCHOOL_ADMIN],
  '/assessments/my': [ROLES.STUDENT],
  // Notices: the School Admin and Principal publish / edit / delete them; EVERY school role reads the ones addressed to it (the API decides which).
  '/notices': [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL],
  '/notices/my': [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL, ROLES.TEACHER, ROLES.ACCOUNTANT, ROLES.STUDENT, ROLES.GUARDIAN, ROLES.ADMISSION_STAFF],
  // Leave: any staff member with a login applies for their own leave; the School Admin and Principal decide staff leave; the class teacher
  // (or a School Admin) records and decides student leave. The API re-checks every action (e.g. WHICH class teacher).
  '/leave/my': [ROLES.TEACHER, ROLES.PRINCIPAL, ROLES.ACCOUNTANT, ROLES.SCHOOL_ADMIN],
  '/leave/requests': [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL],
  // A Student's own leave (apply, follow, cancel while Pending); the class teacher / School Admin still decide it from Student Leave
  '/leave/mine': [ROLES.STUDENT],
  '/leave/students': [ROLES.TEACHER, ROLES.SCHOOL_ADMIN],
  // Timetable: the School Admin builds it (bell schedule + weekly grid per section); a Teacher reads their own
  '/school-setup/periods': [ROLES.SCHOOL_ADMIN],
  '/timetable': [ROLES.SCHOOL_ADMIN],
  '/timetable/my': [ROLES.TEACHER],
  // My Profile: an employee login's own record, read-only (Teacher, Principal, Accountant, Admission Staff). A School Admin login is the school itself, not an employee.
  '/my-profile': [ROLES.TEACHER, ROLES.PRINCIPAL, ROLES.ACCOUNTANT, ROLES.ADMISSION_STAFF],
  // My Profile for a Student login: their own record, read-only (GET /api/Student/me; every other student read stays closed to Student logins)
  '/my-student-profile': [ROLES.STUDENT],
  // A Student's own fees, read-only (GET /api/Student/me/fees); the staff fee screens stay closed to Student logins
  '/my-fees': [ROLES.STUDENT],
  // Examination / Report Card setup (stage 1): the School Admin configures; the Principal reads (read-only); a Teacher reads only the exam schedule. The API enforces the same.
  // The literal routes ("new", "blank") must stay listed BEFORE ":schemeId" so they match first.
  '/examinations/grade-scales': [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL],
  '/examinations/schemes': [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL],
  '/examinations/schemes/new': [ROLES.SCHOOL_ADMIN],
  '/examinations/schemes/blank': [ROLES.SCHOOL_ADMIN],
  '/examinations/schemes/:schemeId': [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL],
  // Stage 5a: report card look and design. The School Admin edits; the Principal reads (the API enforces the same).
  '/examinations/report-card-settings': [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL],
  '/examinations/activity-areas': [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL],
  '/examinations/schedule': [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL, ROLES.TEACHER],
  // Stage 2: marks entry and the class report. The Principal reads; what a Teacher may enter or review is decided per section and subject by the API.
  '/examinations/marks': [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL, ROLES.TEACHER],
  '/examinations/class-report': [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL, ROLES.TEACHER],
  // Stage 3: results and publishing. The School Admin publishes; the Principal and the section's class teacher read (the API decides which sections). A Student reads only their own published cards.
  '/examinations/results': [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL, ROLES.TEACHER],
  '/my-report-cards': [ROLES.STUDENT],
  // Employees (staff records, documents, creating logins): School Admin only
  '/employees': [ROLES.SCHOOL_ADMIN],
  '/employees/new': [ROLES.SCHOOL_ADMIN],
  '/employees/:employeeId': [ROLES.SCHOOL_ADMIN],
  '/employees/:employeeId/edit': [ROLES.SCHOOL_ADMIN],
  // Fee dashboard + reports: read-only for the Principal (the API sends canManage=false; the screens hide drill-down / export)
  '/fee-dashboard': [ROLES.SCHOOL_ADMIN, ROLES.ACCOUNTANT, ROLES.PRINCIPAL],
  // Fee Status of a class teacher's own section (read-only; the API checks WHICH teacher). The School Admin has the fee dashboard and reports instead.
  '/fee-status': [ROLES.TEACHER],
  '/fee-reports/class': [ROLES.SCHOOL_ADMIN, ROLES.ACCOUNTANT, ROLES.PRINCIPAL],

  '/schools': [ROLES.SUPER_ADMIN],
  '/schools/add': [ROLES.SUPER_ADMIN],
  '/schools/:schoolId/edit': [ROLES.SUPER_ADMIN],
  '/schools/:schoolId/view': [ROLES.SUPER_ADMIN],

  // Enquiries from the public website (Contact Us / Request a Demo): Super Admin only; the API re-checks every call
  '/enquiries': [ROLES.SUPER_ADMIN],

  // Subscription & billing (what SMSS charges schools): Super Admin only; the API re-checks every call
  '/subscriptions': [ROLES.SUPER_ADMIN],
  '/invoices': [ROLES.SUPER_ADMIN],

  // Company Finance (SMSS's own employees, expenses, salary payments and financial overview): Super Admin only; the API re-checks every call
  '/company/overview': [ROLES.SUPER_ADMIN],
  '/company/employees': [ROLES.SUPER_ADMIN],
  '/company/expenses': [ROLES.SUPER_ADMIN],

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
