/**
 * The ONLY place in the app that looks at role names. The API's token and /api/Auth/me carry lut_roles NAMES ("Student", "Teacher", ...),
 * not codes. If a role is ever renamed in lut_roles this breaks; a roleCodes field in the auth response is the proper long-term fix.
 * Client-side roles decide what to SHOW. The server is the authority on what is allowed.
 */
export type AppRole = 'STUDENT' | 'TEACHER' | 'DRIVER' | 'SCHOOL_ADMIN' | 'GUARDIAN' | 'UNSUPPORTED';

const STUDENT = 'student';
const TEACHER = 'teacher';
const DRIVER = 'driver';
const SCHOOL_ADMIN = 'school admin';
const GUARDIAN = 'guardian';

// TODO: a role switcher for people who hold several supported roles. v1 picks one: Student first, then Teacher, then Driver.
export function resolveRole(roles: readonly string[] | null | undefined): AppRole {
  const names = new Set((roles ?? []).map((r) => r.trim().toLowerCase()));
  if (names.has(STUDENT)) return 'STUDENT';
  if (names.has(TEACHER)) return 'TEACHER';
  if (names.has(DRIVER)) return 'DRIVER';
  // after the others: a School Admin who is also a Teacher (or Driver, Student) keeps that app
  if (names.has(SCHOOL_ADMIN)) return 'SCHOOL_ADMIN';
  // a parent: the family screens. The server never gives a Guardian login the Student role, and every other role ranks above, so a Guardian-only login is the only way in.
  if (names.has(GUARDIAN)) return 'GUARDIAN';
  return 'UNSUPPORTED';
}
