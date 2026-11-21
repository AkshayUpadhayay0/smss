import { NAV_ITEMS, visibleNav } from '../../layouts/main-layout/nav.config';
import { SETUP_GATED_PREFIXES, isSetupGated, showsSetupProgress } from './setup-gate';

describe('setup gate config', () => {
  it('gates the student, admission, employee and transport-record screens and nothing else', () => {
    for (const url of ['/students', '/students/DPS001ST001', '/students/import', '/admissions', '/admissions/12/edit', '/employees/new', '/transport/areas', '/transport/drivers'])
      expect(isSetupGated(url), url).toBe(true);
    for (const url of ['/dashboard', '/school-setup/classes', '/fee-setup/fee-structures', '/transport/live', '/fee-status', '/students-x', '/calendar', '/employeesx'])
      expect(isSetupGated(url), url).toBe(false);
  });

  it('ignores a query string and fragment', () => {
    expect(isSetupGated('/students?x=1')).toBe(true);
    expect(isSetupGated('/dashboard?next=/students')).toBe(false);
  });

  it('every gated prefix is a real entry of the menu (the list cannot drift from the nav)', () => {
    const routes = NAV_ITEMS.flatMap((i) => (i.children ? i.children.map((c) => c.route) : i.route ? [i.route] : []));
    for (const prefix of SETUP_GATED_PREFIXES) expect(routes.some((r) => r === prefix || r.startsWith(prefix + '/')), prefix).toBe(true);
  });

  it('never gates a setup or fee screen, which is how the user gets out of the lock', () => {
    const setup = NAV_ITEMS.filter((i) => i.label === 'School Setup' || i.label === 'Fee Setup').flatMap((i) => i.children ?? []);
    expect(setup.length).toBeGreaterThan(0);
    for (const c of setup) expect(isSetupGated(c.route), c.route).toBe(false);
  });

  it('shows the progress card on the dashboard and the setup screens only', () => {
    expect(showsSetupProgress('/dashboard')).toBe(true);
    expect(showsSetupProgress('/school-setup/classes')).toBe(true);
    expect(showsSetupProgress('/fee-setup/fee-types')).toBe(true);
    expect(showsSetupProgress('/students')).toBe(false);
  });

  it('a School Admin menu has Employees and Transport as whole locked groups, Students as locked entries', () => {
    const items = visibleNav(['School Admin'], false);
    const group = (label: string) => items.find((i) => i.label === label)!;
    for (const label of ['Employees', 'Transport']) expect(group(label).children!.every((c) => isSetupGated(c.route)), label).toBe(true);
    expect(group('Students').children!.some((c) => isSetupGated(c.route))).toBe(true);
  });
});
