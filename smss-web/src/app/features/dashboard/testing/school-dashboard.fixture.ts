import { SchoolDashboard } from '../models/school-dashboard.model';

/** A complete School Admin dashboard response for specs. Lives in a plain file (not a *.spec.ts) so no spec has to import another spec. */
export const schoolDashboard = (o: Partial<SchoolDashboard> = {}): SchoolDashboard => ({
  today: '2026-11-03',
  academicYearId: 7,
  academicYearName: '2026-27',
  stats: {
    totalStudents: 420, totalEmployees: 35, attendanceMarkedToday: 300, attendancePresentToday: 276, attendancePercentToday: 92, sectionsMarkedToday: 11, sectionsTotal: 12,
    feeCollectedThisMonth: 125000, feeOutstanding: 480000.5, pendingAdmissions: 6,
  },
  attendanceTrend: Array.from({ length: 14 }, (_, i) => ({ date: `2026-10-${String(21 + i > 31 ? 21 + i - 31 : 21 + i).padStart(2, '0')}`, marked: 300, present: 270, presentPercent: 90 })),
  feeCollectionTrend: [
    { month: '2026-06-01', label: 'Jun 2026', amount: 0 },
    { month: '2026-07-01', label: 'Jul 2026', amount: 50000 },
    { month: '2026-08-01', label: 'Aug 2026', amount: 70000 },
    { month: '2026-09-01', label: 'Sep 2026', amount: 90000 },
    { month: '2026-10-01', label: 'Oct 2026', amount: 100000 },
    { month: '2026-11-01', label: 'Nov 2026', amount: 125000 },
  ],
  alerts: { pendingEmployeeLeaves: 2, pendingStudentLeaves: 1, sectionsWithoutClassTeacher: 3, feeOverdueCount: 14, feeOverdueAmount: 56000 },
  upcomingEvents: [],
  recentNotices: [],
  recentAdmissions: [],
  partsUnavailable: [],
  ...o,
});
