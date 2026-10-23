import { TimetableGrid } from '../../timetable/models/timetable.model';
import { combosFromGrid, day, dueLabel, dueState, todayYmd } from './homework-format';

describe('homework-format', () => {
  it('todayYmd is the local calendar date, zero-padded', () => {
    expect(todayYmd(new Date(2026, 0, 5, 23, 59))).toBe('2026-01-05');
    expect(todayYmd(new Date(2026, 11, 31, 0, 1))).toBe('2026-12-31');
  });

  it('day reads the text, so no timezone can shift it', () => {
    expect(day('2026-10-05')).toBe('05 Oct 2026');
    expect(day('2026-12-31')).toBe('31 Dec 2026');
  });

  it('dueState: before today = overdue, today = today, after = upcoming (no status column involved)', () => {
    expect(dueState('2026-10-22', '2026-10-23')).toBe('overdue');
    expect(dueState('2026-10-23', '2026-10-23')).toBe('today');
    expect(dueState('2026-10-24', '2026-10-23')).toBe('upcoming');
    expect(dueState('2027-01-01', '2026-12-31')).toBe('upcoming');
  });

  it('dueLabel counts calendar days, across month ends', () => {
    expect(dueLabel('2026-10-23', '2026-10-23')).toBe('Due today');
    expect(dueLabel('2026-10-24', '2026-10-23')).toBe('Due in 1 day');
    expect(dueLabel('2026-11-02', '2026-10-30')).toBe('Due in 3 days');
    expect(dueLabel('2026-10-22', '2026-10-23')).toBe('Overdue by 1 day');
    expect(dueLabel('2026-09-30', '2026-10-02')).toBe('Overdue by 2 days');
  });

  it('combosFromGrid lists each section + subject once, in class order', () => {
    const entry = (sectionId: number, className: string, sectionName: string, subjectId: number, subjectName: string) =>
      ({ timetableEntryId: 1, academicYearId: 6, classId: 1, className, sectionId, sectionName, dayOfWeek: 1, periodId: 1, periodNumber: 1, subjectId, subjectName, employeeId: 'E', employeeName: 'T' });
    const cell = (e: ReturnType<typeof entry> | null, d: number) => ({ dayOfWeek: d, entry: e });
    const grid = {
      academicYearId: 6, academicYearName: '2026-27', scope: 'teacher', days: [], entryCount: 4,
      rows: [
        { period: { periodId: 1, periodNumber: 1, startTime: '', endTime: '' }, cells: [cell(entry(2, 'Class 2', 'A', 7, 'Maths'), 1), cell(entry(1, 'Class 1', 'B', 7, 'Maths'), 2), cell(null, 3)] },
        { period: { periodId: 2, periodNumber: 2, startTime: '', endTime: '' }, cells: [cell(entry(2, 'Class 2', 'A', 7, 'Maths'), 1), cell(entry(1, 'Class 1', 'B', 8, 'English'), 2)] },
      ],
    } as unknown as TimetableGrid;
    expect(combosFromGrid(grid).map((c) => `${c.sectionLabel}/${c.subjectName}`)).toEqual(['Class 1 – B/English', 'Class 1 – B/Maths', 'Class 2 – A/Maths']);
  });
});
