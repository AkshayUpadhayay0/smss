/** GET /api/Dashboard/school-admin: the redesigned School Admin dashboard. Numbers only. */
export interface DashboardSession {
  academicYearId: number;
  /** The academic year's own name, e.g. "2026-27". */
  name: string;
  startDate: string;
  endDate: string;
}

export interface DashboardEmployees {
  total: number;
  teachers: number;
  accountants: number;
  admissionStaff: number;
  drivers: number;
  other: number;
}

export interface SchoolAdminDashboard {
  today: string;
  monthStart: string;
  monthEnd: string;
  session: DashboardSession | null;

  totalStudents: number;
  studentsPaidThisMonth: number;
  studentsNotPaidThisMonth: number;
  feeCollectedThisMonth: number;
  feeOutstandingThisMonth: number;

  incomeThisMonth: number;
  incomeThisSession: number | null;
  /** false = there is no expenses data yet: show "Not set up yet", never 0. */
  expensesSetUp: boolean;
  expensesThisMonth: number | null;
  expensesThisSession: number | null;
  otherIncomeSetUp: boolean;

  attendanceMarkedToday: number;
  attendancePresentToday: number;
  newAdmissionsThisSession: number | null;
  employees: DashboardEmployees;
  /** 0 = no transport data: the vehicles card is hidden. */
  activeVehicles: number;
}
