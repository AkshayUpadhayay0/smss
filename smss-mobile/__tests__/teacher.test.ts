import type { StudentLeaveRequest, TeacherGrid } from '../src/api/teacher';
import { distinctSectionCount, periodsForDay, splitLeave, teachingOptions } from '../src/features/teacher/derive';
import { checkMark, checkMaxMarks, tallyAttendance, validateAttachment } from '../src/features/teacher/rules';

const entry = (sectionId: number, sectionName: string, className: string, subjectId: number, subjectName: string) => ({
  academicYearId: 41, classId: 50, className, sectionId, sectionName, subjectId, subjectName, roomNo: null as string | null,
});
const grid: TeacherGrid = {
  academicYearId: 41,
  academicYearName: '2026-27',
  rows: [
    { period: { periodId: 1, periodNumber: 1, startTime: '08:30:00', endTime: '09:10:00' }, cells: [
      { dayOfWeek: 1, entry: entry(7, 'B', 'Class 5', 31, 'English') },
      { dayOfWeek: 2, entry: null },
    ] },
    { period: { periodId: 2, periodNumber: 2, startTime: '09:10:00', endTime: '09:50:00' }, cells: [
      { dayOfWeek: 1, entry: entry(6, 'A', 'Class 5', 36, 'EVS') },
      { dayOfWeek: 2, entry: entry(6, 'A', 'Class 5', 31, 'English') },
    ] },
    { period: { periodId: 3, periodNumber: 3, startTime: '09:50:00', endTime: '10:30:00' }, cells: [
      { dayOfWeek: 1, entry: entry(6, 'A', 'Class 5', 31, 'English') },
    ] },
  ],
};

describe('teachingOptions', () => {
  it('lists each section once with the subjects taught there, sorted', () => {
    const opts = teachingOptions(grid);
    expect(opts.map((o) => o.label)).toEqual(['Class 5 · A', 'Class 5 · B']);
    expect(opts[0].subjects.map((s) => s.subjectName)).toEqual(['English', 'EVS']);
    expect(opts[1].subjects.map((s) => s.subjectName)).toEqual(['English']);
    expect(distinctSectionCount(grid)).toBe(2);
  });
  it('is empty for a teacher with no timetable', () => {
    expect(teachingOptions(undefined)).toEqual([]);
    expect(teachingOptions({ academicYearId: 1, academicYearName: '', rows: [] })).toEqual([]);
  });
});

describe('periodsForDay', () => {
  it('returns the day in period order and nothing for a free day', () => {
    expect(periodsForDay(grid, 1).map((p) => [p.periodNumber, p.subjectName, p.sectionLabel])).toEqual([
      [1, 'English', 'Class 5 · B'],
      [2, 'EVS', 'Class 5 · A'],
      [3, 'English', 'Class 5 · A'],
    ]);
    expect(periodsForDay(grid, 2)).toHaveLength(1);
    expect(periodsForDay(grid, 6)).toEqual([]); // Saturday: nothing
  });
});

describe('splitLeave', () => {
  const l = (id: number, status: string) => ({ leaveRequestId: id, status }) as StudentLeaveRequest;
  it('puts Pending in one list and everything decided in the other, newest first', () => {
    const { pending, decided } = splitLeave([l(1, 'Approved'), l(2, 'Pending'), l(3, 'Rejected'), l(4, 'pending'), l(5, 'Cancelled')]);
    expect(pending.map((x) => x.leaveRequestId)).toEqual([4, 2]);
    expect(decided.map((x) => x.leaveRequestId)).toEqual([5, 3, 1]);
  });
});

describe('checkMark', () => {
  it('accepts valid marks including decimals and a comma', () => {
    expect(checkMark('18', 20)).toEqual({ state: 'ok', value: 18 });
    expect(checkMark('7.5', 10)).toEqual({ state: 'ok', value: 7.5 });
    expect(checkMark('7,5', 10)).toEqual({ state: 'ok', value: 7.5 });
    expect(checkMark('0', 10)).toEqual({ state: 'ok', value: 0 });
    expect(checkMark('20', 20)).toEqual({ state: 'ok', value: 20 });
  });
  it('treats blank as empty, not as zero', () => {
    expect(checkMark('', 20).state).toBe('empty');
    expect(checkMark('   ', 20).state).toBe('empty');
  });
  it('rejects marks above the maximum, negatives, text and 3 decimals', () => {
    expect(checkMark('21', 20)).toMatchObject({ state: 'invalid', message: 'Must be at most 20.' });
    expect(checkMark('20.01', 20).state).toBe('invalid');
    expect(checkMark('-1', 20).state).toBe('invalid');
    expect(checkMark('abc', 20).state).toBe('invalid');
    expect(checkMark('1.234', 20)).toMatchObject({ state: 'invalid', message: 'At most two decimal places.' });
  });
});

describe('checkMaxMarks', () => {
  it('validates the maximum', () => {
    expect(checkMaxMarks('20')).toEqual({ value: 20 });
    expect(checkMaxMarks('0')).toHaveProperty('error');
    expect(checkMaxMarks('')).toHaveProperty('error');
    expect(checkMaxMarks('10000')).toHaveProperty('error');
    expect(checkMaxMarks('9999.99')).toEqual({ value: 9999.99 });
    expect(checkMaxMarks('1.234')).toHaveProperty('error');
  });
});

describe('validateAttachment', () => {
  it('allows PDF and images up to 5 MB', () => {
    expect(validateAttachment({ name: 'a.pdf', size: 1000 })).toBeNull();
    expect(validateAttachment({ name: 'photo.JPG', size: 5 * 1024 * 1024 })).toBeNull();
    expect(validateAttachment({ name: 'a.webp' })).toBeNull();
  });
  it('rejects other types, empty files and files over 5 MB', () => {
    expect(validateAttachment({ name: 'a.docx', size: 10 })).toMatch(/PDF or an image/);
    expect(validateAttachment({ name: 'noext', size: 10 })).toMatch(/PDF or an image/);
    expect(validateAttachment({ name: 'a.pdf', size: 0 })).toMatch(/empty/);
    expect(validateAttachment({ name: 'a.pdf', size: 5 * 1024 * 1024 + 1 })).toMatch(/5 MB/);
  });
});

describe('tallyAttendance', () => {
  it('counts explicit marks and the unmarked rest', () => {
    expect(tallyAttendance(['a', 'b', 'c', 'd'], { a: 'PRESENT', b: 'ABSENT', c: 'PRESENT' })).toEqual({ present: 2, absent: 1, unmarked: 1 });
    expect(tallyAttendance([], {})).toEqual({ present: 0, absent: 0, unmarked: 0 });
  });
});
