import { schoolDashboard } from '../testing/school-dashboard.fixture';
import { attendanceTrendOptions, feeCollectionOptions } from './school-dashboard-charts';
import { buildSchoolAlerts, buildSchoolStats, monthName, shortDay } from './school-dashboard-format';

describe('school dashboard format helpers', () => {
  it('monthName / shortDay', () => {
    expect(monthName('2026-11-03')).toBe('November 2026');
    expect(shortDay('2026-10-21')).toBe('21 Oct');
  });

  describe('buildSchoolStats', () => {
    it('builds the six cards in order, each linking to its module', () => {
      const cards = buildSchoolStats(schoolDashboard());
      expect(cards.map((c) => c.id)).toEqual(['students', 'employees', 'attendance', 'collected', 'outstanding', 'admissions']);
      expect(cards.map((c) => c.link)).toEqual(['/students', '/employees', '/attendance/mark', '/fee-dashboard', '/fee-dashboard', '/admissions']);
      expect(cards.map((c) => c.value)).toEqual(['420', '35', '92%', '₹1,25,000.00', '₹4,80,000.50', '6']);
      expect(cards.every((c) => !c.unavailable)).toBe(true);
    });

    it("today's attendance says Not marked yet (not 0%) before anyone is marked, and how many sections are marked", () => {
      const [, , att] = buildSchoolStats(
        schoolDashboard({ stats: { ...schoolDashboard().stats, attendancePercentToday: null, attendanceMarkedToday: 0, attendancePresentToday: 0, sectionsMarkedToday: 0, sectionsTotal: 12 } }),
      );
      expect(att.value).toBe('Not marked yet');
      expect(att.note).toBe('0 of 12 sections marked · 0 present of 0');
    });

    it('notes are singular for one section', () => {
      const [, , att] = buildSchoolStats(schoolDashboard({ stats: { ...schoolDashboard().stats, sectionsTotal: 1, sectionsMarkedToday: 1 } }));
      expect(att.note).toContain('1 of 1 section marked');
    });

    it('a part that could not load reads Unavailable, never 0, and only for the cards that depend on it', () => {
      const cards = buildSchoolStats(
        schoolDashboard({ partsUnavailable: ['fees', 'students'], stats: { ...schoolDashboard().stats, totalStudents: null, feeCollectedThisMonth: null, feeOutstanding: null } }),
      );
      const by = Object.fromEntries(cards.map((c) => [c.id, c]));
      expect(by['students'].value).toBe('Unavailable');
      expect(by['collected'].value).toBe('Unavailable');
      expect(by['outstanding'].unavailable).toBe(true);
      expect(by['employees'].unavailable).toBe(false);
      expect(by['admissions'].unavailable).toBe(false);
      expect(by['attendance'].unavailable).toBe(false);
    });

    it('a genuine zero is shown as 0 / ₹0.00, not as unavailable', () => {
      const cards = buildSchoolStats(schoolDashboard({ stats: { ...schoolDashboard().stats, totalStudents: 0, feeCollectedThisMonth: 0, pendingAdmissions: 0 } }));
      expect(cards[0].value).toBe('0');
      expect(cards[3].value).toBe('₹0.00');
      expect(cards[5].value).toBe('0');
      expect(cards[0].unavailable).toBe(false);
    });
  });

  describe('buildSchoolAlerts', () => {
    it('lists the four alerts with where to act on each (the class-teacher one has no screen yet)', () => {
      const a = buildSchoolAlerts(schoolDashboard());
      expect(a.map((r) => r.id)).toEqual(['employeeLeave', 'studentLeave', 'classTeachers', 'feeOverdue']);
      expect(a.map((r) => r.link)).toEqual(['/leave/requests', '/leave/students', null, '/fee-dashboard']);
      expect(a[0].detail).toBe('2 requests waiting for a decision');
      expect(a[1].detail).toBe('1 request waiting for a decision');
      expect(a[2].detail).toBe('3 sections with no class teacher');
      expect(a[3].detail).toBe('14 installments, ₹56,000.00 past due');
      expect(a.every((r) => !r.clear && !r.unavailable)).toBe(true);
    });

    it('zero counts are "clear" rows with a reassuring line', () => {
      const a = buildSchoolAlerts(schoolDashboard({ alerts: { pendingEmployeeLeaves: 0, pendingStudentLeaves: 0, sectionsWithoutClassTeacher: 0, feeOverdueCount: 0, feeOverdueAmount: 0 } }));
      expect(a.every((r) => r.clear)).toBe(true);
      expect(a.map((r) => r.detail)).toEqual(['No requests waiting', 'No requests waiting', 'Every section has a class teacher', 'Nothing is past due']);
    });

    it('a part that could not load is unavailable, not clear', () => {
      const a = buildSchoolAlerts(schoolDashboard({ partsUnavailable: ['employeeLeave', 'fees'], alerts: { ...schoolDashboard().alerts, pendingEmployeeLeaves: null, feeOverdueCount: null, feeOverdueAmount: null } }));
      expect(a[0]).toMatchObject({ unavailable: true, clear: false, detail: 'Could not be loaded' });
      expect(a[3]).toMatchObject({ unavailable: true, clear: false });
      expect(a[1].unavailable).toBe(false);
    });
  });
});

describe('school dashboard chart options', () => {
  const points = [
    { date: '2026-10-21', marked: 100, present: 90, presentPercent: 90 },
    { date: '2026-10-22', marked: 0, present: 0, presentPercent: null },
    { date: '2026-10-23', marked: 100, present: 75, presentPercent: 75 },
  ];

  it('attendance: a line of present %, 0-100, with an unmarked day left as a gap (null), not 0', () => {
    const o = attendanceTrendOptions(points);
    expect(o.chart?.type).toBe('line');
    expect((o.series as { data: (number | null)[] }[])[0].data).toEqual([90, null, 75]);
    expect(o.xaxis?.categories).toEqual(['21 Oct', '22 Oct', '23 Oct']);
    expect(o.yaxis).toMatchObject({ min: 0, max: 100 });
  });

  it('attendance tooltip says how many were present, or that nothing was marked', () => {
    const f = (attendanceTrendOptions(points).tooltip?.y as { formatter: (v: number | null, o: { dataPointIndex: number }) => string }).formatter;
    expect(f(90, { dataPointIndex: 0 })).toBe('90% (90 of 100)');
    expect(f(null, { dataPointIndex: 1 })).toBe('Not marked');
  });

  it('fees: one bar per month in rupees with compact axis labels', () => {
    const o = feeCollectionOptions(schoolDashboard().feeCollectionTrend);
    expect(o.chart?.type).toBe('bar');
    expect((o.series as { data: number[] }[])[0].data).toEqual([0, 50000, 70000, 90000, 100000, 125000]);
    expect(o.xaxis?.categories).toHaveLength(6);
    const y = (o.yaxis as { labels: { formatter: (v: number) => string } }).labels.formatter;
    expect(y(125000)).toBe('₹1.3L');
  });
});
