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
  it.each([[['School Admin']], [['Principal']], [['Accountant']], [['Super Admin']], [['Guardian']], [[]]])('is unsupported for %j', (roles) => {
    expect(resolveRole(roles)).toBe('UNSUPPORTED');
  });
  it('is unsupported for missing roles', () => {
    expect(resolveRole(undefined)).toBe('UNSUPPORTED');
    expect(resolveRole(null)).toBe('UNSUPPORTED');
  });
});
