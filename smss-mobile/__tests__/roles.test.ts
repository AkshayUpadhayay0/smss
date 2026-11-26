import { resolveRole } from '../src/auth/roles';

describe('resolveRole', () => {
  it('matches role names case-insensitively and trimmed', () => {
    expect(resolveRole(['  STUDENT '])).toBe('STUDENT');
    expect(resolveRole(['teacher'])).toBe('TEACHER');
  });
  it('prefers Student over Teacher when both are held', () => {
    expect(resolveRole(['Teacher', 'Student'])).toBe('STUDENT');
  });
  it('lets a School Admin who is also a Teacher into the Teacher app', () => {
    expect(resolveRole(['School Admin', 'Teacher'])).toBe('TEACHER');
  });
  it('maps the Driver role to the driver app (name matching, like the others)', () => {
    expect(resolveRole(['Driver'])).toBe('DRIVER');
    expect(resolveRole(['  driver '])).toBe('DRIVER');
  });
  it('keeps Student and Teacher ahead of Driver', () => {
    expect(resolveRole(['Driver', 'Student'])).toBe('STUDENT');
    expect(resolveRole(['Driver', 'Teacher'])).toBe('TEACHER');
  });
  it('maps a School Admin to the School Admin home, after the other supported roles', () => {
    expect(resolveRole(['School Admin'])).toBe('SCHOOL_ADMIN');
    expect(resolveRole(['  school admin '])).toBe('SCHOOL_ADMIN');
    expect(resolveRole(['School Admin', 'Driver'])).toBe('DRIVER');
    expect(resolveRole(['School Admin', 'Student'])).toBe('STUDENT');
  });
  it.each([[['Principal']], [['Accountant']], [['Super Admin']], [[]]])('is unsupported for %j', (roles) => {
    expect(resolveRole(roles)).toBe('UNSUPPORTED');
  });
  it('maps a Guardian (a parent) to the family app, case-insensitively, and ranks it below every other supported role', () => {
    expect(resolveRole(['Guardian'])).toBe('GUARDIAN');
    expect(resolveRole(['  guardian '])).toBe('GUARDIAN');
    expect(resolveRole(['Guardian', 'Student'])).toBe('STUDENT');
    expect(resolveRole(['Guardian', 'Teacher'])).toBe('TEACHER');
    expect(resolveRole(['Guardian', 'School Admin'])).toBe('SCHOOL_ADMIN');
    expect(resolveRole(['Guardian', 'Principal'])).toBe('GUARDIAN');
  });
  it('is unsupported for missing roles', () => {
    expect(resolveRole(undefined)).toBe('UNSUPPORTED');
    expect(resolveRole(null)).toBe('UNSUPPORTED');
  });
});
