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
  return [
    statCard({ id: 'today', label: 'Collected today', value: formatInr(s.collectedToday ?? 0), note: 'Payments received today', link: '/fee-dashboard', tone: 'success' }, down(d, 'collectedToday') || s.collectedToday == null),
    statCard({ id: 'month', label: 'Collected this month', value: formatInr(s.collectedThisMonth ?? 0), note: `In ${monthName(d.today)}`, link: '/fee-dashboard', tone: 'success' }, down(d, 'fees') || s.collectedThisMonth == null),
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
  ];
}
