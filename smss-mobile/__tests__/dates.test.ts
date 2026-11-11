import { addDays, apiDayOfWeek, daysInMonth, diffDays, formatFull, formatMonthYear, formatShort, formatTime, monthRange, shiftMonth, weekdayMonFirst } from '../src/lib/dates';
import { classLabel, firstName, formatMarks, initials } from '../src/lib/format';

describe('dates (server-date arithmetic)', () => {
  it('adds days across month and year ends', () => {
    expect(addDays('2026-11-30', 1)).toBe('2026-12-01');
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01');
    expect(addDays('2026-03-01', -1)).toBe('2026-02-28');
  });
  it('diffs days', () => {
    expect(diffDays('2026-11-12', '2026-11-11')).toBe(1);
    expect(diffDays('2026-11-10', '2026-11-11')).toBe(-1);
    expect(diffDays('2026-11-11', '2026-11-11')).toBe(0);
  });
  it('knows the weekday (Monday = 0) and the API day number', () => {
    expect(weekdayMonFirst('2026-11-09')).toBe(0); // Monday
    expect(weekdayMonFirst('2026-11-15')).toBe(6); // Sunday
    expect(apiDayOfWeek('2026-11-14')).toBe(6); // Saturday
  });
  it('counts month length incl. leap Februaries', () => {
    expect(daysInMonth(2026, 2)).toBe(28);
    expect(daysInMonth(2028, 2)).toBe(29);
    expect(daysInMonth(2026, 11)).toBe(30);
  });
  it('builds month ranges and shifts months', () => {
    expect(monthRange(2026, 11)).toEqual({ from: '2026-11-01', to: '2026-11-30' });
    expect(shiftMonth(2026, 1, -1)).toEqual({ year: 2025, month: 12 });
    expect(shiftMonth(2026, 12, 1)).toEqual({ year: 2027, month: 1 });
  });
  it('formats', () => {
    expect(formatShort('2026-11-05')).toBe('5 Nov');
    expect(formatFull('2026-11-05T10:00:00')).toBe('5 Nov 2026');
    expect(formatMonthYear(2026, 11)).toBe('November 2026');
    expect(formatTime('08:30:00')).toBe('8:30 AM');
    expect(formatTime('12:05:00')).toBe('12:05 PM');
    expect(formatTime('00:10:00')).toBe('12:10 AM');
  });
});

describe('format helpers', () => {
  it('makes initials', () => {
    expect(initials('Asha Rao')).toBe('AR');
    expect(initials('  madonna ')).toBe('M');
    expect(initials('A B C')).toBe('AC');
    expect(initials('')).toBe('?');
  });
  it('shows marks as given', () => {
    expect(formatMarks(18)).toBe('18');
    expect(formatMarks(18.5)).toBe('18.5');
    expect(formatMarks(18.25)).toBe('18.25');
  });
  it('builds a class label from what exists', () => {
    expect(classLabel('Class 5', 'A')).toBe('Class 5 · A');
    expect(classLabel('Class 5', null)).toBe('Class 5');
    expect(classLabel(null, null)).toBeNull();
    expect(firstName('Asha Rao')).toBe('Asha');
  });
});
