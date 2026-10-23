// Mirrors LeaveTypeDto / EmployeeLeaveDto / StudentLeaveDto (leave management: request, approve / reject, cancel; no balances).

export type LeaveStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled';
export const LEAVE_STATUSES: readonly LeaveStatus[] = ['Pending', 'Approved', 'Rejected', 'Cancelled'];

export interface LeaveType {
  leaveTypeId: number;
  typeName: string;
  typeCode: string;
  appliesToEmployee: boolean;
  appliesToStudent: boolean;
}

interface LeaveRequestBase {
  leaveRequestId: number;
  leaveTypeId: number;
  leaveTypeName: string;
  /** yyyy-MM-dd */
  fromDate: string;
  toDate: string;
  /** Calendar days, both ends included. */
  days: number;
  reason: string;
  status: LeaveStatus;
  appliedBy: number;
  appliedByName?: string | null;
  appliedAt: string;
  /** Who approved OR rejected it. */
  decidedBy?: number | null;
  decidedByName?: string | null;
  decidedAt?: string | null;
  decisionReason?: string | null;
}

export interface EmployeeLeave extends LeaveRequestBase {
  employeeId: string;
  employeeName: string;
}

export interface StudentLeave extends LeaveRequestBase {
  studentId: string;
  studentName: string;
  sectionId?: number | null;
  className?: string | null;
  sectionName?: string | null;
}

export interface ApplyEmployeeLeaveRequest {
  /** Only a School Admin may name another employee; leave out to apply for yourself. */
  employeeId?: string;
  leaveTypeId: number;
  fromDate: string;
  toDate: string;
  reason: string;
}

export interface ApplyStudentLeaveRequest {
  studentId: string;
  leaveTypeId: number;
  fromDate: string;
  toDate: string;
  reason: string;
}

export interface DecideLeaveRequest {
  decision: 'Approve' | 'Reject';
  /** Required when rejecting. */
  decisionReason?: string;
}

/** A row of a requests table: display-ready values. */
export interface LeaveRow extends Record<string, unknown> {
  leaveRequestId: number;
  person: string;
  personId: string;
  /** Class – section (students only). */
  group: string;
  leaveTypeName: string;
  dates: string;
  days: number;
  reason: string;
  status: LeaveStatus;
  appliedBy: string;
  decision: string;
}
