import type { SchoolAdminDashboard } from '../../api/admin';

const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

/** "2026-11-01" -> "November 2026" (string maths: no Date, no time-zone surprises). */
export function monthLabel(isoDate: string): string {
  const [y, m] = isoDate.split('-').map(Number);
  return `${MONTHS[m - 1] ?? ''} ${y}`;
}

/** Indian digit grouping, written by hand because Intl locale support differs between Android and iOS: 1234567 -> "12,34,567". */
export function groupIndian(n: number): string {
  const s = String(Math.abs(Math.trunc(n)));
  const last3 = s.slice(-3);
  const rest = s.slice(0, -3);
  const grouped = rest ? rest.replace(/\B(?=(\d{2})+(?!\d))/g, ',') + ',' + last3 : last3;
  return (n < 0 ? '-' : '') + grouped;
}

/** Rupees: whole amounts without paise, otherwise two decimals. 2500 -> "₹2,500", 1234.5 -> "₹1,234.50". */
export function inr(amount: number): string {
  if (Number.isInteger(amount)) return '₹' + groupIndian(amount);
  const [whole, paise] = Math.abs(amount).toFixed(2).split('.');
  return (amount < 0 ? '-' : '') + '₹' + groupIndian(Number(whole)) + '.' + paise;
}

export type CardTone = 'primary' | 'success' | 'danger' | 'warning' | 'info';

export interface AdminCard {
  id: string;
  label: string;
  value: string;
  note: string;
  tone: CardTone;
  /** "Not set up yet" and similar: shown quieter, never as a number. */
  muted?: boolean;
}

export function feeCards(d: SchoolAdminDashboard): AdminCard[] {
  const month = monthLabel(d.monthStart);
  return [
    { id: 'total-students', label: 'Total students', value: String(d.totalStudents), note: 'Active students', tone: 'primary' },
    { id: 'paid', label: 'Paid this month', value: String(d.studentsPaidThisMonth), note: `Fully paid, ${month}`, tone: 'success' },
    // The web card opens the Class-wise Fee Report; the app has no fee report screen, so this is a plain card.
    { id: 'not-paid', label: 'Not paid this month', value: String(d.studentsNotPaidThisMonth), note: 'Unpaid or part-paid', tone: 'danger' },
    { id: 'collected', label: 'Fee collected this month', value: inr(d.feeCollectedThisMonth), note: 'Cash received, net of concession', tone: 'info' },
    { id: 'outstanding', label: 'Outstanding this month', value: inr(d.feeOutstandingThisMonth), note: "Unpaid balance of this month's dues", tone: 'warning' },
  ];
}

export function moneyCards(d: SchoolAdminDashboard): AdminCard[] {
  const month = monthLabel(d.monthStart);
  const session = d.session ? `Academic Year ${d.session.name}` : 'No current academic year';
  const notSet = (id: string, label: string): AdminCard => ({ id, label, value: 'Not set up yet', note: 'Expenses are not recorded in SMSS yet', tone: 'warning', muted: true });
  return [
    { id: 'income-month', label: 'Income this month', value: inr(d.incomeThisMonth), note: `${month}, till today`, tone: 'success' },
    d.session
      ? { id: 'income-session', label: 'Income this session', value: inr(d.incomeThisSession ?? 0), note: `${session}, till today`, tone: 'success' }
      : { id: 'income-session', label: 'Income this session', value: 'Not set up yet', note: 'Add a current academic year', tone: 'warning', muted: true },
    d.expensesSetUp ? { id: 'expenses-month', label: 'Expenses this month', value: inr(d.expensesThisMonth ?? 0), note: `${month}, till today`, tone: 'danger' } : notSet('expenses-month', 'Expenses this month'),
    d.expensesSetUp ? { id: 'expenses-session', label: 'Expenses this session', value: inr(d.expensesThisSession ?? 0), note: `${session}, till today`, tone: 'danger' } : notSet('expenses-session', 'Expenses this session'),
  ];
}

export function attendanceCard(d: SchoolAdminDashboard): AdminCard {
  if (d.attendanceMarkedToday <= 0) return { id: 'attendance', label: 'Attendance today', value: 'Not marked yet', note: 'No attendance has been marked today', tone: 'warning', muted: true };
  return { id: 'attendance', label: 'Attendance today', value: `${d.attendancePresentToday} present`, note: `out of ${d.attendanceMarkedToday} students marked today`, tone: 'success' };
}

export function admissionsCard(d: SchoolAdminDashboard): AdminCard {
  return d.newAdmissionsThisSession === null
    ? { id: 'new-admissions', label: 'New admissions this session', value: 'Not set up yet', note: 'Add a current academic year', tone: 'warning', muted: true }
    : { id: 'new-admissions', label: 'New admissions this session', value: String(d.newAdmissionsThisSession), note: `Academic Year ${d.session?.name ?? ''}`.trim(), tone: 'primary' };
}

export function employeeLines(d: SchoolAdminDashboard): { label: string; count: number }[] {
  const e = d.employees;
  return [
    { label: 'Teachers', count: e.teachers },
    { label: 'Accountants', count: e.accountants },
    { label: 'Admission staff', count: e.admissionStaff },
    { label: 'Drivers', count: e.drivers },
    { label: 'Other staff', count: e.other },
  ];
}

/** The vehicles card only exists for a school that has transport data. */
export const showVehicles = (d: SchoolAdminDashboard): boolean => d.activeVehicles > 0;

export const isEmptySchool = (d: SchoolAdminDashboard): boolean => d.totalStudents === 0 && d.employees.total === 0;
