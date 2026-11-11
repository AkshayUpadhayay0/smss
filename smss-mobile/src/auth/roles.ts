/**
 * The ONLY place in the app that looks at role names. The API's token and /api/Auth/me carry lut_roles NAMES ("Student", "Teacher", ...),
 * not codes. If a role is ever renamed in lut_roles this breaks; a roleCodes field in the auth response is the proper long-term fix.
 * Client-side roles decide what to SHOW. The server is the authority on what is allowed.
 */
export type AppRole = 'STUDENT' | 'TEACHER' | 'UNSUPPORTED';

const STUDENT = 'student';
const TEACHER = 'teacher';

// TODO: a role switcher for people who hold several supported roles. v1 picks one: Student first, then Teacher.
export function resolveRole(roles: readonly string[] | null | undefined): AppRole {
  const names = new Set((roles ?? []).map((r) => r.trim().toLowerCase()));
  if (names.has(STUDENT)) return 'STUDENT';
  if (names.has(TEACHER)) return 'TEACHER';
  return 'UNSUPPORTED';
}
