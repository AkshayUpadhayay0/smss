import { formatInr } from '../../../core/utils/currency';
import { SchoolDashboard, SchoolDashboardPart } from '../models/school-dashboard.model';

export interface SchoolStatCard {
  id: 'students' | 'employees' | 'attendance' | 'collected' | 'outstanding' | 'admissions';
  label: string;
  value: string;
  /** The line under the value. */
  note: string;
  link: string;
  tone: 'primary' | 'success' | 'info' | 'warning' | 'danger';
  /** The number could not be loaded: shown as "Unavailable", never as 0. */
  unavailable: boolean;
}

export interface SchoolAlert {
  id: 'employeeLeave' | 'studentLeave' | 'classTeachers' | 'feeOverdue';
  title: string;
  detail: string;
  /** Where to act on it; null when no screen exists for it yet. */
  link: string | null;
  /** Nothing needs attention. */
  clear: boolean;
  unavailable: boolean;
}

const UNAVAILABLE = 'Could not be loaded';

const plural = (n: number, one: string, many = one + 's') => `${n} ${n === 1 ? one : many}`;

/** "October 2026" for the month a yyyy-MM-dd date is in. */
export function monthName(ymd: string): string {
  return new Date(`${ymd.slice(0, 7)}-01T00:00:00`).toLocaleDateString('en-GB', { month: 'long', year: 'numeric' });
}

/** "21 Oct" for the attendance chart's axis. */
export function shortDay(ymd: string): string {
  return new Date(`${ymd}T00:00:00`).toLocaleDateString('en-GB', { day: '2-digit', month: 'short' });
}

const down = (d: SchoolDashboard, ...parts: SchoolDashboardPart[]) => parts.some((p) => d.partsUnavailable.includes(p));

/** The six headline cards, in order. A card whose part could not load says so instead of showing a number. */
export function buildSchoolStats(d: SchoolDashboard): SchoolStatCard[] {
  const s = d.stats;
  const card = (c: Omit<SchoolStatCard, 'unavailable'>, isDown: boolean): SchoolStatCard =>
    isDown ? { ...c, value: 'Unavailable', note: UNAVAILABLE, unavailable: true } : { ...c, unavailable: false };

  const attendanceNote =
    s.sectionsTotal == null
      ? `${s.attendancePresentToday ?? 0} present of ${s.attendanceMarkedToday ?? 0} marked`
      : `${s.sectionsMarkedToday ?? 0} of ${plural(s.sectionsTotal, 'section')} marked · ${s.attendancePresentToday ?? 0} present of ${s.attendanceMarkedToday ?? 0}`;

  return [
    card({ id: 'students', label: 'Total students', value: String(s.totalStudents ?? 0), note: d.academicYearName ? `Active in ${d.academicYearName}` : 'Active students', link: '/students', tone: 'primary' }, down(d, 'students') || s.totalStudents == null),
    card({ id: 'employees', label: 'Total employees', value: String(s.totalEmployees ?? 0), note: 'Active staff', link: '/employees', tone: 'info' }, down(d, 'employees') || s.totalEmployees == null),
    card(
      {
        id: 'attendance',
        label: "Today's attendance",
        value: s.attendancePercentToday == null ? 'Not marked yet' : `${s.attendancePercentToday}%`,
        note: attendanceNote,
        link: '/attendance/mark',
        tone: 'success',
      },
      down(d, 'attendance'),
    ),
    card({ id: 'collected', label: 'Fee collected this month', value: formatInr(s.feeCollectedThisMonth ?? 0), note: `In ${monthName(d.today)}`, link: '/fee-dashboard', tone: 'success' }, down(d, 'fees') || s.feeCollectedThisMonth == null),
    card({ id: 'outstanding', label: 'Fee outstanding', value: formatInr(s.feeOutstanding ?? 0), note: d.academicYearName ? `Still to collect for ${d.academicYearName}` : 'Still to collect', link: '/fee-dashboard', tone: 'warning' }, down(d, 'fees') || s.feeOutstanding == null),
    card({ id: 'admissions', label: 'Pending admissions', value: String(s.pendingAdmissions ?? 0), note: 'Submitted or under review', link: '/admissions', tone: 'danger' }, down(d, 'admissions') || s.pendingAdmissions == null),
  ];
}

/** The four Alerts & Reminders rows. A count of 0 is a "clear" row, not a missing one. */
export function buildSchoolAlerts(d: SchoolDashboard): SchoolAlert[] {
  const a = d.alerts;
  const row = (id: SchoolAlert['id'], title: string, count: number | null, isDown: boolean, text: (n: number) => string, none: string, link: string | null): SchoolAlert => {
    if (isDown || count == null) return { id, title, detail: UNAVAILABLE, link, clear: false, unavailable: true };
    return { id, title, detail: count > 0 ? text(count) : none, link, clear: count === 0, unavailable: false };
  };
  return [
    row('employeeLeave', 'Employee leave awaiting approval', a.pendingEmployeeLeaves, down(d, 'employeeLeave'), (n) => `${plural(n, 'request')} waiting for a decision`, 'No requests waiting', '/leave/requests'),
    row('studentLeave', 'Student leave awaiting approval', a.pendingStudentLeaves, down(d, 'studentLeave'), (n) => `${plural(n, 'request')} waiting for a decision`, 'No requests waiting', '/leave/students'),
    // there is no Class-Teacher screen yet, so this row is information only
    row('classTeachers', 'Sections without a class teacher', a.sectionsWithoutClassTeacher, down(d, 'classTeachers'), (n) => `${plural(n, 'section')} with no class teacher`, 'Every section has a class teacher', null),
    row(
      'feeOverdue',
      'Fee installments overdue',
      a.feeOverdueCount,
      down(d, 'fees'),
      (n) => `${plural(n, 'installment')}${a.feeOverdueAmount ? ', ' + formatInr(a.feeOverdueAmount) + ' past due' : ''}`,
      'Nothing is past due',
      '/fee-dashboard',
    ),
  ];
}
