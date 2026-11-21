import { SchoolAdminDashboard } from '../models/school-admin-dashboard.model';
import { attendanceView, employeeLines, incomeExpenseOptions, inr, isEmptyDashboard, moneyCards, monthLabel, monthParam, paidDonutOptions, studentsAndFeesCards } from './school-admin-dashboard-format';

export const dashboard = (over: Partial<SchoolAdminDashboard> = {}): SchoolAdminDashboard => ({
  today: '2026-11-30', monthStart: '2026-11-01', monthEnd: '2026-11-30',
  session: { academicYearId: 3, name: '2026-27', startDate: '2026-04-01', endDate: '2027-03-31' },
  totalStudents: 6, studentsPaidThisMonth: 2, studentsNotPaidThisMonth: 2, feeCollectedThisMonth: 2500, feeOutstandingThisMonth: 1100,
  incomeThisMonth: 2500, incomeThisSession: 2700, expensesSetUp: false, expensesThisMonth: null, expensesThisSession: null, otherIncomeSetUp: false,
  attendanceMarkedToday: 3, attendancePresentToday: 2, newAdmissionsThisSession: 3,
  employees: { total: 6, teachers: 2, accountants: 1, admissionStaff: 1, drivers: 1, other: 1 }, activeVehicles: 2,
  ...over,
});

describe('school admin dashboard formatting', () => {
  it('formats rupees with Indian grouping, whole rupees without paise', () => {
    expect(inr(0)).toBe('₹0');
    expect(inr(2500)).toBe('₹2,500');
    expect(inr(1234567)).toBe('₹12,34,567');
    expect(inr(1234.5)).toBe('₹1,234.50');
  });

  it('names the month from the ISO date and gives the report its yyyy-MM', () => {
    expect(monthLabel('2026-11-01')).toBe('November 2026');
    expect(monthLabel('2027-01-01')).toBe('January 2027');
    expect(monthParam('2026-11-01')).toBe('2026-11');
  });

  describe('students & fees cards', () => {
    const cards = studentsAndFeesCards(dashboard());
    const byId = (id: string) => cards.find((c) => c.id === id)!;

    it('has the five cards in order with their figures', () => {
      expect(cards.map((c) => c.id)).toEqual(['total-students', 'paid', 'not-paid', 'collected', 'outstanding']);
      expect(byId('total-students').value).toBe('6');
      expect(byId('paid').value).toBe('2');
      expect(byId('not-paid').value).toBe('2');
      expect(byId('collected').value).toBe('₹2,500');
      expect(byId('outstanding').value).toBe('₹1,100');
    });

    it('only the not-paid card links, to the class-wise fee report filtered to the current month', () => {
      expect(cards.filter((c) => c.link).map((c) => c.id)).toEqual(['not-paid']);
      expect(byId('not-paid').link).toEqual({ path: '/fee-reports/class', query: { month: '2026-11' } });
    });

    it('says in a tooltip what each money card includes', () => {
      expect(byId('collected').hint).toContain('not voided');
      expect(byId('collected').hint).toContain('concession');
      expect(byId('paid').hint).toContain('discounted due counts as paid');
      expect(byId('outstanding').hint).toContain('due date is in this month');
    });
  });

  describe('money cards', () => {
    it('shows "Not set up yet" for expenses (never 0) and says income is fees only', () => {
      const cards = moneyCards(dashboard());
      const exp = cards.filter((c) => c.id.startsWith('expenses'));
      expect(exp).toHaveLength(2);
      for (const c of exp) { expect(c.value).toBe('Not set up yet'); expect(c.muted).toBe(true); expect(c.value).not.toContain('₹'); }
      expect(cards.find((c) => c.id === 'income-month')!.hint).toContain('Other income is not set up');
    });

    it('names the session as "Academic Year 2026-27"', () => {
      const c = moneyCards(dashboard()).find((x) => x.id === 'income-session')!;
      expect(c.value).toBe('₹2,700');
      expect(c.note).toContain('Academic Year 2026-27');
    });

    it('shows expense figures once expenses exist', () => {
      const cards = moneyCards(dashboard({ expensesSetUp: true, expensesThisMonth: 400, expensesThisSession: 1200 }));
      expect(cards.find((c) => c.id === 'expenses-month')!.value).toBe('₹400');
      expect(cards.find((c) => c.id === 'expenses-session')!.value).toBe('₹1,200');
    });

    it('does not invent a session figure when there is no current academic year', () => {
      const c = moneyCards(dashboard({ session: null, incomeThisSession: null })).find((x) => x.id === 'income-session')!;
      expect(c.value).toBe('Not set up yet');
      expect(c.muted).toBe(true);
    });
  });

  it('attendance reads "X present out of Y" or "Not marked yet", never 0 of 0', () => {
    expect(attendanceView(dashboard())).toEqual({ marked: true, value: '2 present', note: 'out of 3 students marked today' });
    const none = attendanceView(dashboard({ attendanceMarkedToday: 0, attendancePresentToday: 0 }));
    expect(none.marked).toBe(false);
    expect(none.value).toBe('Not marked yet');
  });

  it('lists the employee groups in a fixed order', () => {
    expect(employeeLines(dashboard()).map((l) => `${l.label}:${l.count}`)).toEqual(['Teachers:2', 'Accountants:1', 'Admission staff:1', 'Drivers:1', 'Other staff:1']);
  });

  it('a school with no students and no staff is "empty"', () => {
    expect(isEmptyDashboard(dashboard())).toBe(false);
    expect(isEmptyDashboard(dashboard({ totalStudents: 0, employees: { total: 0, teachers: 0, accountants: 0, admissionStaff: 0, drivers: 0, other: 0 } }))).toBe(true);
  });

  describe('charts', () => {
    it('the donut is paid vs not paid', () => {
      const o = paidDonutOptions(dashboard());
      expect(o.series).toEqual([2, 2]);
      expect(o.labels).toEqual(['Paid', 'Not paid']);
    });

    it('income vs expenses leaves expenses out while they are not set up (no fake zero bars)', () => {
      const o = incomeExpenseOptions(dashboard());
      expect((o.series as { name: string }[]).map((s) => s.name)).toEqual(['Income']);
      expect((o.series as { data: number[] }[])[0].data).toEqual([2500, 2700]);
    });

    it('draws both series once expenses exist, and only this month when there is no session', () => {
      const both = incomeExpenseOptions(dashboard({ expensesSetUp: true, expensesThisMonth: 400, expensesThisSession: 1200 }));
      expect((both.series as { name: string }[]).map((s) => s.name)).toEqual(['Income', 'Expenses']);
      const noSession = incomeExpenseOptions(dashboard({ session: null, incomeThisSession: null }));
      expect((noSession.series as { data: number[] }[])[0].data).toEqual([2500]);
    });
  });
});
