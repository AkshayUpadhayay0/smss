import type { ApexOptions } from 'ng-apexcharts';
import { formatInr } from '../../../core/utils/currency';
import { SchoolAdminDashboard } from '../models/school-admin-dashboard.model';
import { ChartColors, FALLBACK_COLORS, compactInr } from './super-admin-charts';

const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

/** "2026-11-01" -> "November 2026" (no Date object: no time-zone surprises). */
export function monthLabel(isoDate: string): string {
  const [y, m] = isoDate.split('-').map(Number);
  return `${MONTHS[m - 1] ?? ''} ${y}`;
}

/** "2026-11-01" -> "2026-11", the month the Class-wise Fee Report filters on. */
export function monthParam(isoDate: string): string {
  return isoDate.slice(0, 7);
}

/** Indian digit grouping, whole rupees: 1234567 -> "₹12,34,567". Paise only when there are some. */
export function inr(amount: number): string {
  return Number.isInteger(amount) ? '₹' + amount.toLocaleString('en-IN') : formatInr(amount);
}

export interface DashboardCard {
  id: string;
  label: string;
  value: string;
  note: string;
  /** What the card counts, shown as a tooltip. */
  hint: string;
  icon: 'users' | 'circle-check' | 'circle-alert' | 'indian-rupee' | 'wallet' | 'coins' | 'calendar-check' | 'user-plus' | 'truck' | 'user';
  tone: 'primary' | 'success' | 'danger' | 'warning' | 'info';
  /** In-app link; only the not-paid card has one. */
  link?: { path: string; query: Record<string, string> };
  /** "Not set up yet" and similar: shown in smaller type, never as a number. */
  muted?: boolean;
}

/** The fee figures of a year with no fee items: not zeros, a plain "no fee structure yet". */
function noFeeStructure(id: string, label: string, d: SchoolAdminDashboard): DashboardCard {
  return {
    id, label, value: 'No fee structure yet', note: d.session ? `Nothing is assigned for ${d.session.name}` : 'No academic year', icon: 'indian-rupee', tone: 'warning', muted: true,
    hint: 'No fee has been assigned to any student for this academic year yet, so there is nothing to count (this is not zero).',
  };
}

export function studentsAndFeesCards(d: SchoolAdminDashboard): DashboardCard[] {
  const month = monthLabel(d.monthStart);
  const year = d.session?.name ?? 'the academic year';
  const students: DashboardCard = {
    id: 'total-students', label: 'Total students', value: String(d.totalStudents), note: d.session ? `Enrolled in ${year}` : 'No academic year',
    hint: `Active students with an Active enrolment in ${year}.`, icon: 'users', tone: 'primary',
  };
  if (!d.hasFeeSetup) {
    return [students, noFeeStructure('paid', 'Paid this month', d), noFeeStructure('not-paid', 'Not paid this month', d), noFeeStructure('collected', 'Fees received this month', d), noFeeStructure('outstanding', 'Outstanding this month', d)];
  }
  return [
    students,
    {
      id: 'paid', label: 'Paid this month', value: String(d.studentsPaidThisMonth), note: `Fully paid, ${month}`, icon: 'circle-check', tone: 'success',
      hint: 'Students with at least one fee due this month where every due this month is fully paid (a discounted due counts as paid). Students with no due this month are not counted.',
    },
    {
      id: 'not-paid', label: 'Not paid this month', value: String(d.studentsNotPaidThisMonth), note: 'Unpaid or part-paid. Tap to see the report', icon: 'circle-alert', tone: 'danger',
      hint: 'Students with at least one due this month that is unpaid or part-paid. Opens the Class-wise Fee Report for this month.',
      link: { path: '/fee-reports/class', query: { month: monthParam(d.monthStart) } },
    },
    {
      id: 'collected', label: 'Fees received this month', value: inr(d.feeCollectedThisMonth), note: `For ${year} fees, ${month}`, icon: 'indian-rupee', tone: 'info',
      hint: `Cash received this month against ${year} installments, on payments that are not voided, net of any concession given at payment. Tax is not included. Installments of another academic year paid this month are not in this figure, so it is not the total cash received.`,
    },
    {
      id: 'outstanding', label: 'Outstanding this month', value: inr(d.feeOutstandingThisMonth), note: 'Unpaid balance of this month\'s dues', icon: 'wallet', tone: 'warning',
      hint: `The unpaid balance of ${year} fee dues whose due date is in this month, for Active students.`,
    },
  ];
}

export function moneyCards(d: SchoolAdminDashboard): DashboardCard[] {
  const month = monthLabel(d.monthStart);
  const session = d.session ? `Academic Year ${d.session.name}` : 'No current academic year';
  const incomeHint = d.otherIncomeSetUp
    ? 'Fees received for this academic year plus other income.'
    : 'Fees received against this academic year\'s installments (cash, net of concession). Other income is not set up yet, so it is not included. It is not the total cash received: another year\'s installments paid at the same time are not here.';
  const notSet = (id: string, label: string): DashboardCard => ({
    id, label, value: 'Not set up yet', note: 'Expenses are not recorded in SMSS yet', hint: 'There is no school expenses record yet, so no figure is shown (this is not zero).',
    icon: 'coins', tone: 'warning', muted: true,
  });
  return [
    d.hasFeeSetup
      ? { id: 'income-month', label: 'Fees received this month', value: inr(d.incomeThisMonth), note: `For ${d.session?.name ?? 'the year'}, ${month}`, hint: incomeHint, icon: 'indian-rupee', tone: 'success' as const }
      : noFeeStructure('income-month', 'Fees received this month', d),
    d.session && !d.hasFeeSetup ? noFeeStructure('income-session', 'Fees received for this session', d)
    : d.session
      ? { id: 'income-session', label: 'Fees received for this session', value: inr(d.incomeThisSession ?? 0), note: session, hint: incomeHint, icon: 'indian-rupee', tone: 'success' as const }
      : { id: 'income-session', label: 'Fees received for this session', value: 'Not set up yet', note: 'Add a current academic year', hint: 'The session is the current academic year.', icon: 'indian-rupee', tone: 'warning' as const, muted: true },
    d.expensesSetUp ? { id: 'expenses-month', label: 'Expenses this month', value: inr(d.expensesThisMonth ?? 0), note: `${month}, till today`, hint: 'Expenses recorded this month.', icon: 'coins', tone: 'danger' as const } : notSet('expenses-month', 'Expenses this month'),
    d.expensesSetUp ? { id: 'expenses-session', label: 'Expenses this session', value: inr(d.expensesThisSession ?? 0), note: `${session}, till today`, hint: 'Expenses recorded this session.', icon: 'coins', tone: 'danger' as const } : notSet('expenses-session', 'Expenses this session'),
  ];
}

export interface AttendanceView {
  marked: boolean;
  value: string;
  note: string;
}

export function attendanceView(d: SchoolAdminDashboard): AttendanceView {
  if (d.attendanceMarkedToday <= 0) return { marked: false, value: 'Not marked yet', note: 'No attendance has been marked today' };
  return { marked: true, value: `${d.attendancePresentToday} present`, note: `out of ${d.attendanceMarkedToday} students marked today` };
}

export interface EmployeeLine {
  label: string;
  count: number;
}

export function employeeLines(d: SchoolAdminDashboard): EmployeeLine[] {
  const e = d.employees;
  return [
    { label: 'Teachers', count: e.teachers },
    { label: 'Accountants', count: e.accountants },
    { label: 'Admission staff', count: e.admissionStaff },
    { label: 'Drivers', count: e.drivers },
    { label: 'Other staff', count: e.other },
  ];
}

/** True when there is nothing to show yet: no students and no employees (a brand-new school). */
export function isEmptyDashboard(d: SchoolAdminDashboard): boolean {
  return d.totalStudents === 0 && d.employees.total === 0;
}

const BASE_CHART = { fontFamily: 'inherit', toolbar: { show: false }, zoom: { enabled: false }, animations: { enabled: true, speed: 400 } };

/** Paid vs not paid students this month. */
export function paidDonutOptions(d: SchoolAdminDashboard, c: ChartColors = FALLBACK_COLORS): ApexOptions {
  return {
    chart: { ...BASE_CHART, type: 'donut', height: 240 },
    series: [d.studentsPaidThisMonth, d.studentsNotPaidThisMonth],
    labels: ['Paid', 'Not paid'],
    colors: [c.success, c.danger],
    dataLabels: { enabled: false },
    legend: { position: 'bottom' },
    plotOptions: { pie: { donut: { size: '68%', labels: { show: true, name: { fontSize: '12px', offsetY: -4 }, value: { fontSize: '22px', offsetY: 2 }, total: { show: true, label: 'With dues', fontSize: '12px', formatter: () => String(d.studentsPaidThisMonth + d.studentsNotPaidThisMonth) } } } } },
    stroke: { width: 2 },
  } as ApexOptions;
}

/** Income vs expenses, this month and this session. Expenses are left out entirely while they are not set up (no fake zero bars). */
export function incomeExpenseOptions(d: SchoolAdminDashboard, c: ChartColors = FALLBACK_COLORS): ApexOptions {
  const cats = d.session ? ['This month', 'This session'] : ['This month'];
  const income = d.session ? [d.incomeThisMonth, d.incomeThisSession ?? 0] : [d.incomeThisMonth];
  const series = [{ name: 'Fees received', data: income }];
  const colors = [c.success];
  if (d.expensesSetUp) {
    series.push({ name: 'Expenses', data: d.session ? [d.expensesThisMonth ?? 0, d.expensesThisSession ?? 0] : [d.expensesThisMonth ?? 0] });
    colors.push(c.danger);
  }
  return {
    chart: { ...BASE_CHART, type: 'bar', height: 260 },
    series,
    colors,
    plotOptions: { bar: { borderRadius: 8, borderRadiusApplication: 'end', columnWidth: '45%' } },
    dataLabels: { enabled: false },
    xaxis: { categories: cats, axisBorder: { show: false }, axisTicks: { show: false } },
    yaxis: { min: 0, labels: { formatter: (v: number) => compactInr(v) } },
    grid: { borderColor: c.grid, strokeDashArray: 4 },
    legend: { position: 'bottom' },
    tooltip: { y: { formatter: (v: number) => formatInr(v) } },
  } as ApexOptions;
}
