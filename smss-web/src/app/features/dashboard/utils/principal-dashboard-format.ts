import { formatInr } from '../../../core/utils/currency';
import { PrincipalDashboard, PrincipalDashboardPart } from '../models/principal-dashboard.model';
import { DashAlert, DashStatCard, alertRow, plural, statCard } from './role-dashboard-format';

const down = (d: PrincipalDashboard, ...parts: PrincipalDashboardPart[]) => parts.some((p) => d.partsUnavailable.includes(p));

/** Collected as a share of what is due, one decimal; "—" when nothing is billed yet. */
export function collectionRate(collected: number, due: number): string {
  return due > 0 ? `${Math.round((collected * 1000) / due) / 10}%` : '—';
}

/**
 * The six headline cards. The Principal reads the Fee dashboard's numbers (collected / outstanding for the current academic year) but has no screen to collect or drill
 * into, so the fee cards link to the read-only Fee Dashboard and the head-count cards have no link.
 */
export function buildPrincipalStats(d: PrincipalDashboard): DashStatCard[] {
  const s = d.stats;
  const year = d.academicYearName;
  return [
    statCard({ id: 'students', label: 'Total students', value: String(s.totalStudents ?? 0), note: year ? `Active in ${year}` : 'Active students', link: null, tone: 'primary' }, down(d, 'students') || s.totalStudents == null),
    statCard({ id: 'staff', label: 'Total staff', value: String(s.totalStaff ?? 0), note: 'Active employees', link: null, tone: 'info' }, down(d, 'staff') || s.totalStaff == null),
    statCard(
      {
        id: 'attendance',
        label: "Today's attendance",
        value: s.attendancePercentToday == null ? 'Not marked yet' : `${s.attendancePercentToday}%`,
        note: `${s.attendancePresentToday ?? 0} present of ${s.attendanceMarkedToday ?? 0} marked`,
        link: '/attendance/report',
        tone: 'success',
      },
      down(d, 'attendance'),
    ),
    statCard({ id: 'collected', label: 'Fee collected', value: formatInr(s.feeCollected ?? 0), note: year ? `In ${year}` : 'This academic year', link: '/fee-dashboard', tone: 'success' }, down(d, 'fees') || s.feeCollected == null),
    statCard({ id: 'outstanding', label: 'Fee outstanding', value: formatInr(s.feeOutstanding ?? 0), note: year ? `Still to collect for ${year}` : 'Still to collect', link: '/fee-dashboard', tone: 'warning' }, down(d, 'fees') || s.feeOutstanding == null),
    statCard(
      { id: 'rate', label: 'Collection rate', value: collectionRate(s.feeCollected ?? 0, s.feeTotalDue ?? 0), note: `of ${formatInr(s.feeTotalDue ?? 0)} billed`, link: '/fee-dashboard', tone: 'primary' },
      down(d, 'fees') || s.feeCollected == null || s.feeTotalDue == null,
    ),
  ];
}

/** Things waiting for the Principal. A count of 0 is a "clear" row, not a missing one. */
export function buildPrincipalAlerts(d: PrincipalDashboard): DashAlert[] {
  const a = d.alerts;
  return [
    alertRow('employeeLeave', 'Employee leave awaiting your decision', a.pendingEmployeeLeaves, down(d, 'employeeLeave'), (n) => `${plural(n, 'request')} waiting for a decision`, 'No requests waiting', '/leave/requests'),
    alertRow('admissionsDecide', 'Admissions to decide', a.admissionsToDecide, down(d, 'admissions'), (n) => `${plural(n, 'application')} under review, ready to approve or reject`, 'No applications waiting for a decision', '/admissions'),
    alertRow('admissionsReview', 'Admissions awaiting review', a.admissionsAwaitingReview, down(d, 'admissions'), (n) => `${plural(n, 'new application')} not yet taken into review`, 'No new applications', '/admissions'),
  ];
}
