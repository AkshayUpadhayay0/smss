import { MonthlyAttendanceReport, ReportMark } from '../models/attendance-report.model';

const CODE: Record<ReportMark, string> = { Present: 'P', Absent: 'A', NotMarked: '-', NotEnrolled: '', NonWorking: '' };

/** One CSV field: quoted when it holds a comma, quote or line break; a leading = + - @ is neutralised so a spreadsheet never runs a student's name as a formula. */
export function csvField(value: string | number | null | undefined): string {
  let s = value === null || value === undefined ? '' : String(value);
  if (/^[=+@]/.test(s) || (/^-/.test(s) && s.length > 1 && !/^-\d/.test(s))) s = `'${s}`;
  return /[",\r\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
}

/** The report as CSV text: Roll, Student, one column per day (P present, A absent, - not marked, blank = not counted), Present, Absent, Attendance %. */
export function reportToCsv(r: MonthlyAttendanceReport): string {
  const head = ['Roll', 'Student', ...r.days.map((d) => String(Number(d.date.slice(8, 10)))), 'Present', 'Absent', 'Attendance %'];
  const rows = r.students.map((s) => [
    s.rollNumber ?? '',
    s.isActive ? s.fullName : `${s.fullName} (left)`,
    ...s.marks.map((m, i) => (m === 'NotMarked' && r.days[i].isFuture ? '' : CODE[m])),
    s.presentDays,
    s.absentDays,
    s.percentage ?? '',
  ]);
  return [head, ...rows].map((row) => row.map(csvField).join(',')).join('\r\n') + '\r\n';
}

/** attendance-Nursery-A-2026-11.csv */
export function reportFileName(r: MonthlyAttendanceReport, ext: 'csv' | 'pdf'): string {
  const safe = `${r.className}-${r.sectionName}`.replace(/[^A-Za-z0-9]+/g, '-').replace(/^-|-$/g, '');
  return `attendance-${safe}-${r.year}-${String(r.month).padStart(2, '0')}.${ext}`;
}
