import { formatInr } from '../../../core/utils/currency';
import { AccountantDashboard, AccountantDashboardPart } from '../models/accountant-dashboard.model';
import { DashStatCard, plural, statCard } from './role-dashboard-format';

const down = (d: AccountantDashboard, ...parts: AccountantDashboardPart[]) => parts.some((p) => d.partsUnavailable.includes(p));

/** "October 2026" for the month a yyyy-MM-dd date is in. */
const monthName = (ymd: string): string => new Date(`${ymd.slice(0, 7)}-01T00:00:00`).toLocaleString('en-IN', { month: 'long', year: 'numeric' });

/**
 * The five headline cards. The Accountant collects fees, so every card goes to the screen where the work happens: the Fee Dashboard (drill-down) or the class-wise report.
 * Overdue and upcoming say how many installments AND how much; a card whose part could not load says "Unavailable", never 0.
 */
export function buildAccountantStats(d: AccountantDashboard): DashStatCard[] {
  const s = d.stats;
  // the viewed year has no fee items at all: its dues are not zero, there is nothing to count
  const noFees = (id: string, label: string, tone: 'warning' | 'danger' | 'info'): DashStatCard => ({
    id, label, value: 'No fee structure yet', note: d.academicYearName ? `Nothing is assigned for ${d.academicYearName}` : 'No academic year', link: '/fee-reports/class', tone, unavailable: false,
  });
  const dues = !d.hasFeeSetup && !down(d, 'fees');
  return [
    statCard({ id: 'today', label: 'Cash collected today', value: formatInr(s.collectedToday ?? 0), note: 'Fees only, all years, received today (late fee and tax are below)', link: '/fee-dashboard', tone: 'success' }, down(d, 'collectedToday') || s.collectedToday == null),
    statCard({ id: 'month', label: 'Cash collected this month', value: formatInr(s.collectedThisMonth ?? 0), note: `Fees only, all years, in ${monthName(d.today)}`, link: '/fee-dashboard', tone: 'success' }, down(d, 'fees') || s.collectedThisMonth == null),
    statCard({ id: 'late-today', label: 'Late fee collected today', value: formatInr(s.lateFeeToday ?? 0), note: 'Late fee only, all years (not in the cash collected above)', link: '/fee-reports/late-fee', tone: 'success' }, down(d, 'lateFee') || s.lateFeeToday == null),
    statCard({ id: 'late-month', label: 'Late fee collected this month', value: formatInr(s.lateFeeThisMonth ?? 0), note: `Late fee only, all years, in ${monthName(d.today)}`, link: '/fee-reports/late-fee', tone: 'success' }, down(d, 'lateFee') || s.lateFeeThisMonth == null),
    statCard({ id: 'received-today', label: 'Total received today', value: formatInr(s.totalReceivedToday ?? 0), note: 'Fee + late fee + tax on today\'s receipts, not voided', link: '/fee-dashboard', tone: 'info' }, down(d, 'lateFee') || s.totalReceivedToday == null),
    statCard({ id: 'received-month', label: 'Total received this month', value: formatInr(s.totalReceivedThisMonth ?? 0), note: `Fee + late fee + tax on the receipts of ${monthName(d.today)}, not voided`, link: '/fee-dashboard', tone: 'info' }, down(d, 'lateFee') || s.totalReceivedThisMonth == null),
    ...(dues ? [noFees('outstanding', 'Outstanding', 'warning'), noFees('overdue', 'Overdue', 'danger'), noFees('upcoming', `Due in the next ${s.upcomingDays} days`, 'info')] : [
    statCard({ id: 'outstanding', label: 'Outstanding', value: formatInr(s.outstanding ?? 0), note: d.academicYearName ? `Still to collect for ${d.academicYearName}` : 'Still to collect', link: '/fee-reports/class', tone: 'warning' }, down(d, 'fees') || s.outstanding == null),
    statCard(
      {
        id: 'overdue',
        label: 'Overdue',
        value: s.overdueCount === 0 ? 'Nothing overdue' : formatInr(s.overdueAmount ?? 0),
        note: s.overdueCount === 0 ? 'Every due date so far is covered' : `${plural(s.overdueCount ?? 0, 'installment')} past due`,
        link: '/fee-reports/class',
        tone: 'danger',
      },
      down(d, 'fees') || s.overdueCount == null,
    ),
    statCard(
      {
        id: 'upcoming',
        label: `Due in the next ${s.upcomingDays} days`,
        value: s.upcomingCount === 0 ? 'Nothing due' : formatInr(s.upcomingAmount ?? 0),
        note: s.upcomingCount === 0 ? 'No installments fall due soon' : `${plural(s.upcomingCount ?? 0, 'installment')} falling due`,
        link: '/fee-reports/class',
        tone: 'info',
      },
      down(d, 'fees') || s.upcomingCount == null,
    ),
    ]),
  ];
}
