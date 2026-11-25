/** GET /api/Dashboard/school-admin: the redesigned School Admin dashboard. Numbers only. */
export interface DashboardSession {
  academicYearId: number;
  /** The academic year's own name, e.g. "2026-27". */
  name: string;
  startDate: string;
  endDate: string;
  /** The school's current year (the one new fees and enrolments go to). */
  isCurrent: boolean;
  /** A closed year is read-only history. */
  isClosed: boolean;
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
  /** The academic year being VIEWED (the one asked for, else the current year). */
  session: DashboardSession | null;
  /** The school's current year name, to say "viewing 2027-28, current is 2026-27". */
  currentYearName: string | null;
  /** false = the viewed year has no fee items at all: the fee figures are not real zeros, show "no fee structure yet". */
  hasFeeSetup: boolean;
  /** true = today is outside the viewed year, so this month was moved into the year's range; monthNote says so in words. */
  monthClamped: boolean;
  /** true = the school has no WhatsApp number yet: a prompt to add it on the school profile. It never blocks anything. */
  whatsAppMissing: boolean;
  monthNote: string | null;

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
  /** Students missing at least one required document; null = it could not be worked out (the tile says Unavailable, never 0). */
  studentsWithPendingDocuments: number | null;
  /** Primary guardians still lacking a WhatsApp number or an email ("Parent details to complete"); null = it could not be worked out (the tile says Unavailable, never 0). */
  parentDetailsToComplete: number | null;
  employees: DashboardEmployees;
  /** 0 = no transport data: the vehicles card is hidden. */
  activeVehicles: number;
}
