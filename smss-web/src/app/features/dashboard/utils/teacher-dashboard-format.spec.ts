import { TeacherDashboard } from '../models/teacher-dashboard.model';
import { NOT_APPLICABLE, NOT_MARKED_YET, UNAVAILABLE, buildTeacherStats, sectionLabel, unmarkedNote } from './teacher-dashboard-format';

const section = (id: number, cls: string, sec = 'A') => ({ classId: id, className: cls, sectionId: id, sectionName: sec });

const dash = (o: Partial<TeacherDashboard> = {}): TeacherDashboard => ({
  today: '2026-10-27',
  profile: { employeeId: 'DPS001EM001', fullName: 'Anita Verma', designationName: 'Senior Teacher', departmentName: 'Science', photoUrl: null },
  classTeacherOf: [section(1, 'Class 5')],
  subjectsTaught: ['Maths'],
  totalStudents: 30,
  feePending: 4,
  presentToday: 26,
  absentToday: 3,
  notMarkedYet: false,
  unmarkedSections: 0,
  partsUnavailable: [],
  ...o,
});

const values = (d: TeacherDashboard) => buildTeacherStats(d).map((s) => s.value);

describe('buildTeacherStats', () => {
  it('shows the four numbers, in the order Total Students, Fee Pending, Present, Absent', () => {
    const s = buildTeacherStats(dash());
    expect(s.map((x) => x.label)).toEqual(['Total Students', 'Fee Pending', 'Present Today', 'Absent Today']);
    expect(values(dash())).toEqual(['30', '4', '26', '3']);
    expect(s.every((x) => !x.isText && x.note === null)).toBe(true);
  });

  it('says "Not applicable" (never 0) on every card when the teacher is not a class teacher', () => {
    const d = dash({ classTeacherOf: [], totalStudents: null, feePending: null, presentToday: null, absentToday: null });
    expect(values(d)).toEqual([NOT_APPLICABLE, NOT_APPLICABLE, NOT_APPLICABLE, NOT_APPLICABLE]);
    expect(buildTeacherStats(d).every((x) => x.isText)).toBe(true);
  });

  it('keeps a real 0 as "0" (an empty roster is not "not applicable")', () => {
    expect(values(dash({ totalStudents: 0, feePending: 0, presentToday: 0, absentToday: 0 }))).toEqual(['0', '0', '0', '0']);
  });

  it('says "Not marked yet today" on Present and Absent when nothing is marked, and keeps students and fees', () => {
    const d = dash({ notMarkedYet: true, presentToday: null, absentToday: null });
    expect(values(d)).toEqual(['30', '4', NOT_MARKED_YET, NOT_MARKED_YET]);
  });

  it('with some sections marked: the summed numbers plus how many sections are still unmarked', () => {
    const s = buildTeacherStats(dash({ classTeacherOf: [section(1, 'Class 5'), section(2, 'Class 6')], unmarkedSections: 1 }));
    expect(s[2].value).toBe('26');
    expect(s[2].note).toBe('1 section is not marked yet today');
    expect(s[3].note).toBe('1 section is not marked yet today');
    expect(s[0].note).toBeNull();
  });

  it('pluralises the unmarked note and shows nothing for zero', () => {
    expect(unmarkedNote(2)).toBe('2 sections are not marked yet today');
    expect(unmarkedNote(1)).toBe('1 section is not marked yet today');
    expect(unmarkedNote(0)).toBeNull();
  });

  it('says "Unavailable" for a part the server could not load, and leaves the others alone', () => {
    expect(values(dash({ feePending: null, partsUnavailable: ['feePending'] }))).toEqual(['30', UNAVAILABLE, '26', '3']);
    expect(values(dash({ totalStudents: null, partsUnavailable: ['totalStudents', 'attendance'] }))).toEqual([UNAVAILABLE, '4', UNAVAILABLE, UNAVAILABLE]);
  });

  it('says "Unavailable" everywhere (not "Not applicable") when the class-teacher list itself failed', () => {
    const d = dash({ classTeacherOf: [], totalStudents: null, feePending: null, presentToday: null, absentToday: null, partsUnavailable: ['classTeacherOf', 'subjectsTaught'] });
    expect(values(d)).toEqual([UNAVAILABLE, UNAVAILABLE, UNAVAILABLE, UNAVAILABLE]);
  });
});

describe('sectionLabel', () => {
  it('joins class and section', () => expect(sectionLabel(section(1, 'Class 5', 'B'))).toBe('Class 5 – B'));
  it('falls back to the class alone', () => expect(sectionLabel(section(1, 'Class 5', ''))).toBe('Class 5'));
});
