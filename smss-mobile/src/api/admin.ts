import { api } from './index';

// Typed endpoint module for the SCHOOL ADMIN home. The school always comes from the token: nothing here sends a school id.

export interface AdminEmployees {
  total: number;
  teachers: number;
  accountants: number;
  admissionStaff: number;
  drivers: number;
  other: number;
}

/** GET /api/Dashboard/school-admin. Numbers only; the server counts everything. "Today" and "this month" are India dates. */
export interface SchoolAdminDashboard {
  today: string;
  monthStart: string;
  monthEnd: string;
  session: { academicYearId: number; name: string; startDate: string; endDate: string } | null;
  totalStudents: number;
  studentsPaidThisMonth: number;
  studentsNotPaidThisMonth: number;
  feeCollectedThisMonth: number;
  feeOutstandingThisMonth: number;
  incomeThisMonth: number;
  incomeThisSession: number | null;
  /** false = there is no expenses data: show "Not set up yet", never 0. */
  expensesSetUp: boolean;
  expensesThisMonth: number | null;
  expensesThisSession: number | null;
  otherIncomeSetUp: boolean;
  attendanceMarkedToday: number;
  attendancePresentToday: number;
  newAdmissionsThisSession: number | null;
  employees: AdminEmployees;
  /** 0 = the school has no transport data: the card is hidden. */
  activeVehicles: number;
}

export const getSchoolAdminDashboard = () => api.request<SchoolAdminDashboard>('/api/Dashboard/school-admin');
