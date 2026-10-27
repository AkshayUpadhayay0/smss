import { TeacherDashboard, TeacherSection } from '../models/teacher-dashboard.model';

export const NOT_APPLICABLE = 'Not applicable';
export const NOT_MARKED_YET = 'Not marked yet today';
export const UNAVAILABLE = 'Unavailable';

export type StatTone = 'violet' | 'amber' | 'green' | 'red';

export interface TeacherStat {
  key: 'students' | 'fees' | 'present' | 'absent';
  label: string;
  /** A number, or one of the words above. */
  value: string;
  /** true when value is a word, not a number (shown smaller and muted). */
  isText: boolean;
  /** Small line under the label, e.g. how many sections are still unmarked. */
  note: string | null;
  tone: StatTone;
}

/** "Class 2 – A" */
export function sectionLabel(s: TeacherSection): string {
  return s.sectionName ? `${s.className} – ${s.sectionName}` : s.className;
}

/** "1 section is not marked yet today" / "2 sections are not marked yet today"; null when nothing is outstanding. */
export function unmarkedNote(count: number): string | null {
  if (count <= 0) return null;
  return count === 1 ? '1 section is not marked yet today' : `${count} sections are not marked yet today`;
}

/**
 * The four summary cards. Rules:
 *  - not a class teacher -> every card says "Not applicable" (never 0);
 *  - the class-teacher list itself could not be loaded -> "Unavailable" (we cannot tell whether it applies);
 *  - Present / Absent: "Not marked yet today" when nothing is marked; with several sections and only some marked, the sums of the marked ones
 *    plus a note saying how many are still unmarked.
 */
export function buildTeacherStats(d: TeacherDashboard): TeacherStat[] {
  const out = (key: TeacherStat['key'], label: string, tone: StatTone, value: string, isText: boolean, note: string | null = null): TeacherStat => ({
    key,
    label,
    tone,
    value,
    isText,
    note,
  });
  const cards: [TeacherStat['key'], string, StatTone][] = [
    ['students', 'Total Students', 'violet'],
    ['fees', 'Fee Pending', 'amber'],
    ['present', 'Present Today', 'green'],
    ['absent', 'Absent Today', 'red'],
  ];

  if (d.partsUnavailable.includes('classTeacherOf')) return cards.map(([k, l, t]) => out(k, l, t, UNAVAILABLE, true));
  if (d.classTeacherOf.length === 0) return cards.map(([k, l, t]) => out(k, l, t, NOT_APPLICABLE, true));

  const count = (n: number | null, unavailable: boolean, k: TeacherStat['key'], l: string, t: StatTone, note: string | null = null) =>
    unavailable || n == null ? out(k, l, t, UNAVAILABLE, true) : out(k, l, t, String(n), false, note);

  const attendanceDown = d.partsUnavailable.includes('attendance');
  const students = count(d.totalStudents, d.partsUnavailable.includes('totalStudents'), 'students', 'Total Students', 'violet');
  const fees = count(d.feePending, d.partsUnavailable.includes('feePending'), 'fees', 'Fee Pending', 'amber');

  let present: TeacherStat;
  let absent: TeacherStat;
  if (attendanceDown) {
    present = out('present', 'Present Today', 'green', UNAVAILABLE, true);
    absent = out('absent', 'Absent Today', 'red', UNAVAILABLE, true);
  } else if (d.notMarkedYet) {
    present = out('present', 'Present Today', 'green', NOT_MARKED_YET, true);
    absent = out('absent', 'Absent Today', 'red', NOT_MARKED_YET, true);
  } else {
    const note = unmarkedNote(d.unmarkedSections);
    present = count(d.presentToday, false, 'present', 'Present Today', 'green', note);
    absent = count(d.absentToday, false, 'absent', 'Absent Today', 'red', note);
  }
  return [students, fees, present, absent];
}
