import { ROLES } from '../../core/config/route-roles';
import { visibleNav } from './nav.config';

const labels = (roles: string[]) => visibleNav(roles).map((i) => i.label);
const group = (roles: string[]) => visibleNav(roles).find((i) => i.label === 'Students' && i.children);
const childLabels = (roles: string[]) => (group(roles)?.children ?? []).map((c) => c.label);

describe('visibleNav: Students group', () => {
  it('School Admin gets the collapsible group with every student child, and no flat copies', () => {
    expect(childLabels([ROLES.SCHOOL_ADMIN])).toEqual(['Students', 'Admissions', 'Add Existing Student', 'Student Leave']);
    const flat = visibleNav([ROLES.SCHOOL_ADMIN]).filter((i) => !i.children).map((i) => i.label);
    for (const l of ['Admissions', 'Add Existing Student', 'Student Leave', 'My Students']) expect(flat).not.toContain(l);
  });

  it('Teacher sees My Students and Student Leave in the group', () => {
    expect(childLabels([ROLES.TEACHER])).toEqual(['My Students', 'Student Leave']);
  });

  it('a role that would see one child gets it as a flat item (Admission Staff, Principal -> Admissions)', () => {
    for (const role of [ROLES.ADMISSION_STAFF, ROLES.PRINCIPAL]) {
      expect(group([role])).toBeUndefined();
      const item = visibleNav([role]).find((i) => i.label === 'Admissions');
      expect(item?.route).toBe('/admissions');
      expect(item?.children).toBeUndefined();
    }
  });

  it('roles with no student pages see nothing of the group', () => {
    for (const role of [ROLES.ACCOUNTANT, ROLES.STUDENT, ROLES.GUARDIAN, ROLES.SUPER_ADMIN]) {
      expect(group([role])).toBeUndefined();
      expect(labels([role])).not.toContain('Admissions');
    }
  });

  it('every child route is still guarded by the same ROUTE_ROLES (presentation only)', () => {
    expect(group([ROLES.SCHOOL_ADMIN])?.children?.map((c) => c.route)).toEqual(['/students', '/admissions', '/students/onboard-existing', '/leave/students']);
  });
});

describe('visibleNav: Student sidebar', () => {
  it('a Student gets My Profile and none of the staff student pages', () => {
    const l = labels([ROLES.STUDENT]);
    expect(l).toContain('My Profile');
    for (const x of ['Students', 'Admissions', 'Add Existing Student', 'Student Leave', 'My Students']) expect(l).not.toContain(x);
  });

  it('nobody else gets the Student My Profile item', () => {
    for (const role of [ROLES.SCHOOL_ADMIN, ROLES.TEACHER, ROLES.PRINCIPAL, ROLES.ACCOUNTANT, ROLES.GUARDIAN]) expect(labels([role])).not.toContain('My Profile');
  });
});

describe('visibleNav: My Leave', () => {
  it('is hidden for a login with no employee record, even with an allowed role', () => {
    expect(labels([ROLES.SCHOOL_ADMIN])).toContain('My Leave'); // default: roles only
    expect(visibleNav([ROLES.SCHOOL_ADMIN], false).map((i) => i.label)).not.toContain('My Leave');
  });

  it('stays for an employee login, including one that also holds School Admin', () => {
    expect(visibleNav([ROLES.SCHOOL_ADMIN], true).map((i) => i.label)).toContain('My Leave');
    expect(visibleNav([ROLES.TEACHER], true).map((i) => i.label)).toContain('My Leave');
  });
});
