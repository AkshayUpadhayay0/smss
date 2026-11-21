import type { SchoolAdminDashboard } from '../src/api/admin';
import { admissionsCard, attendanceCard, employeeLines, feeCards, groupIndian, inr, isEmptySchool, moneyCards, monthLabel, showVehicles } from '../src/features/admin/derive';

const dashboard = (over: Partial<SchoolAdminDashboard> = {}): SchoolAdminDashboard => ({
  today: '2026-11-30',
  monthStart: '2026-11-01',
  monthEnd: '2026-11-30',
  session: { academicYearId: 3, name: '2026-27', startDate: '2026-04-01', endDate: '2027-03-31' },
  totalStudents: 6,
  studentsPaidThisMonth: 2,
  studentsNotPaidThisMonth: 2,
  feeCollectedThisMonth: 2500,
  feeOutstandingThisMonth: 1100,
  incomeThisMonth: 2500,
  incomeThisSession: 2700,
  expensesSetUp: false,
  expensesThisMonth: null,
  expensesThisSession: null,
  otherIncomeSetUp: false,
  attendanceMarkedToday: 3,
  attendancePresentToday: 2,
  newAdmissionsThisSession: 3,
  employees: { total: 6, teachers: 2, accountants: 1, admissionStaff: 1, drivers: 1, other: 1 },
  activeVehicles: 2,
  ...over,
});

describe('rupees', () => {
  it('groups digits the Indian way', () => {
    expect(groupIndian(0)).toBe('0');
    expect(groupIndian(999)).toBe('999');
    expect(groupIndian(1000)).toBe('1,000');
    expect(groupIndian(123456)).toBe('1,23,456');
    expect(groupIndian(1234567)).toBe('12,34,567');
    expect(groupIndian(-1234567)).toBe('-12,34,567');
  });
  it('shows whole rupees without paise and fractions with two decimals', () => {
    expect(inr(2500)).toBe('₹2,500');
    expect(inr(1234.5)).toBe('₹1,234.50');
    expect(inr(0)).toBe('₹0');
  });
});

it('names the month from the ISO date', () => {
  expect(monthLabel('2026-11-01')).toBe('November 2026');
  expect(monthLabel('2027-01-01')).toBe('January 2027');
});

describe('students and fees cards', () => {
  const cards = feeCards(dashboard());
  const by = (id: string) => cards.find((c) => c.id === id)!;
  it('are the five cards with the API figures', () => {
    expect(cards.map((c) => c.id)).toEqual(['total-students', 'paid', 'not-paid', 'collected', 'outstanding']);
    expect(by('total-students').value).toBe('6');
    expect(by('paid').value).toBe('2');
    expect(by('not-paid').value).toBe('2');
    expect(by('collected').value).toBe('₹2,500');
    expect(by('outstanding').value).toBe('₹1,100');
  });
});

describe('money cards', () => {
  it('say "Not set up yet" for expenses, never a zero', () => {
    const exp = moneyCards(dashboard()).filter((c) => c.id.startsWith('expenses'));
    expect(exp).toHaveLength(2);
    for (const c of exp) {
      expect(c.value).toBe('Not set up yet');
      expect(c.muted).toBe(true);
      expect(c.value).not.toContain('₹');
    }
  });
  it('name the session and show expenses once they exist', () => {
    const cards = moneyCards(dashboard({ expensesSetUp: true, expensesThisMonth: 400, expensesThisSession: 1200 }));
    expect(cards.find((c) => c.id === 'income-session')!.note).toContain('Academic Year 2026-27');
    expect(cards.find((c) => c.id === 'expenses-month')!.value).toBe('₹400');
    expect(cards.find((c) => c.id === 'expenses-session')!.value).toBe('₹1,200');
  });
  it('do not invent a session figure without a current academic year', () => {
    const c = moneyCards(dashboard({ session: null, incomeThisSession: null })).find((x) => x.id === 'income-session')!;
    expect(c.value).toBe('Not set up yet');
  });
});

describe('people cards', () => {
  it('attendance reads "X present out of Y" or "Not marked yet"', () => {
    expect(attendanceCard(dashboard())).toMatchObject({ value: '2 present', note: 'out of 3 students marked today' });
    expect(attendanceCard(dashboard({ attendanceMarkedToday: 0, attendancePresentToday: 0 }))).toMatchObject({ value: 'Not marked yet', muted: true });
  });
  it('new admissions need a current academic year', () => {
    expect(admissionsCard(dashboard()).value).toBe('3');
    expect(admissionsCard(dashboard({ session: null, newAdmissionsThisSession: null })).value).toBe('Not set up yet');
  });
  it('lists the employee groups in a fixed order', () => {
    expect(employeeLines(dashboard()).map((l) => `${l.label}:${l.count}`)).toEqual(['Teachers:2', 'Accountants:1', 'Admission staff:1', 'Drivers:1', 'Other staff:1']);
  });
  it('shows the vehicles card only when the school has vehicles', () => {
    expect(showVehicles(dashboard())).toBe(true);
    expect(showVehicles(dashboard({ activeVehicles: 0 }))).toBe(false);
  });
  it('a school with no students and no staff is empty', () => {
    expect(isEmptySchool(dashboard())).toBe(false);
    expect(isEmptySchool(dashboard({ totalStudents: 0, employees: { total: 0, teachers: 0, accountants: 0, admissionStaff: 0, drivers: 0, other: 0 } }))).toBe(true);
  });
});
