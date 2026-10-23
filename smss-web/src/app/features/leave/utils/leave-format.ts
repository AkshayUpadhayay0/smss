import { EmployeeLeave, LeaveRow, LeaveStatus, StudentLeave } from '../models/leave.model';

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** yyyy-MM-dd -> "05 Oct 2026" (read from the text, never through a Date, so no timezone can shift the day). */
export const day = (ymd: string): string => `${ymd.slice(8, 10)} ${MONTHS[Number(ymd.slice(5, 7)) - 1]} ${ymd.slice(0, 4)}`;

export const dateRange = (from: string, to: string): string => (from === to ? day(from) : `${day(from)} – ${day(to)}`);

/** Pill colour for a status (see the table's 'badge' column). */
export const statusTone = (value: unknown): string =>
  ({ Pending: 'yellow', Approved: 'green', Rejected: 'red', Cancelled: 'gray' } as Record<string, string>)[String(value)] ?? 'gray';

const decisionText = (r: { status: LeaveStatus; decidedByName?: string | null; decisionReason?: string | null }): string => {
  if (r.status === 'Pending') return '';
  if (r.status === 'Cancelled') return 'Cancelled';
  const who = r.decidedByName ? `${r.status} by ${r.decidedByName}` : r.status;
  return r.decisionReason ? `${who}: ${r.decisionReason}` : who;
};

export function employeeRow(r: EmployeeLeave): LeaveRow {
  return {
    leaveRequestId: r.leaveRequestId, person: r.employeeName, personId: r.employeeId, group: '', leaveTypeName: r.leaveTypeName, dates: dateRange(r.fromDate, r.toDate), days: r.days,
    reason: r.reason, status: r.status, appliedBy: r.appliedByName ?? '', decision: decisionText(r),
  };
}

export function studentRow(r: StudentLeave): LeaveRow {
  return {
    leaveRequestId: r.leaveRequestId, person: r.studentName, personId: r.studentId, group: r.className && r.sectionName ? `${r.className} – ${r.sectionName}` : '', leaveTypeName: r.leaveTypeName,
    dates: dateRange(r.fromDate, r.toDate), days: r.days, reason: r.reason, status: r.status, appliedBy: r.appliedByName ?? '', decision: decisionText(r),
  };
}
