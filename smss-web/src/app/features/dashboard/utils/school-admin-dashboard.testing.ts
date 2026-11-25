import { SchoolAdminDashboard } from '../models/school-admin-dashboard.model';

/** A School Admin dashboard answer for specs. (Kept out of the .spec files: a spec that imports another spec is collected twice and sometimes reports "No test suite found".) */
export const dashboard = (over: Partial<SchoolAdminDashboard> = {}): SchoolAdminDashboard => ({
  today: '2026-11-30', monthStart: '2026-11-01', monthEnd: '2026-11-30',
  session: { academicYearId: 3, name: '2026-27', startDate: '2026-04-01', endDate: '2027-03-31', isCurrent: true, isClosed: false },
  currentYearName: '2026-27', hasFeeSetup: true, monthClamped: false, monthNote: null, whatsAppMissing: false,
  totalStudents: 6, studentsPaidThisMonth: 2, studentsNotPaidThisMonth: 2, feeCollectedThisMonth: 2500, feeOutstandingThisMonth: 1100,
  incomeThisMonth: 2500, incomeThisSession: 2700, expensesSetUp: false, expensesThisMonth: null, expensesThisSession: null, otherIncomeSetUp: false,
  attendanceMarkedToday: 3, attendancePresentToday: 2, newAdmissionsThisSession: 3, studentsWithPendingDocuments: 4, parentDetailsToComplete: 6,
  employees: { total: 6, teachers: 2, accountants: 1, admissionStaff: 1, drivers: 1, other: 1 }, activeVehicles: 2,
  ...over,
});
