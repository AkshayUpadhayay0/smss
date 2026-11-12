import type { StudentLeaveRequest, TeacherGrid } from '../../api/teacher';
import { classLabel } from '../../lib/format';

// Pure helpers that turn the teacher's own timetable (GET /Timetable/my) into pickers and "my periods today". No server rules are re-implemented
// here: the server re-checks on create that the teacher really teaches that subject to that section.

export interface SubjectOption {
  subjectId: number;
  subjectName: string;
}
export interface SectionOption {
  sectionId: number;
  classId: number;
  /** "Class 5 · A" */
  label: string;
  subjects: SubjectOption[];
}

/** Every section the teacher teaches, each with the subjects they teach to it. Sorted by class then section name. */
export function teachingOptions(grid: TeacherGrid | undefined): SectionOption[] {
  const bySection = new Map<number, SectionOption & { sortKey: string }>();
  for (const row of grid?.rows ?? []) {
    for (const cell of row.cells) {
      const e = cell.entry;
      if (!e) continue;
      let s = bySection.get(e.sectionId);
      if (!s) {
        s = { sectionId: e.sectionId, classId: e.classId, label: classLabel(e.className, e.sectionName) ?? `Section ${e.sectionId}`, subjects: [], sortKey: `${e.className}\u0000${e.sectionName}` };
        bySection.set(e.sectionId, s);
      }
      if (!s.subjects.some((x) => x.subjectId === e.subjectId)) s.subjects.push({ subjectId: e.subjectId, subjectName: e.subjectName });
    }
  }
  return [...bySection.values()]
    .sort((a, b) => a.sortKey.localeCompare(b.sortKey, undefined, { numeric: true }))
    .map(({ sortKey: _sortKey, ...rest }) => ({ ...rest, subjects: [...rest.subjects].sort((a, b) => a.subjectName.localeCompare(b.subjectName)) }));
}

export interface PeriodToday {
  periodNumber: number;
  startTime: string;
  endTime: string;
  subjectName: string;
  sectionLabel: string;
  roomNo: string | null;
}

/** The teacher's periods on one weekday (1 = Monday .. 6 = Saturday), in period order. Pass the weekday of the SERVER's date. */
export function periodsForDay(grid: TeacherGrid | undefined, dayOfWeek: number): PeriodToday[] {
  const out: PeriodToday[] = [];
  for (const row of grid?.rows ?? []) {
    const e = row.cells.find((c) => c.dayOfWeek === dayOfWeek)?.entry;
    if (!e) continue;
    out.push({
      periodNumber: row.period.periodNumber,
      startTime: row.period.startTime,
      endTime: row.period.endTime,
      subjectName: e.subjectName,
      sectionLabel: classLabel(e.className, e.sectionName) ?? '',
      roomNo: e.roomNo,
    });
  }
  return out.sort((a, b) => a.periodNumber - b.periodNumber);
}

export function distinctSectionCount(grid: TeacherGrid | undefined): number {
  return teachingOptions(grid).length;
}

const PENDING = 'pending';

/** Pending vs Decided (Approved / Rejected / Cancelled), by the status text the server derived. Newest first within each. */
export function splitLeave(list: StudentLeaveRequest[]): { pending: StudentLeaveRequest[]; decided: StudentLeaveRequest[] } {
  const sorted = [...list].sort((a, b) => b.leaveRequestId - a.leaveRequestId);
  return {
    pending: sorted.filter((l) => l.status.trim().toLowerCase() === PENDING),
    decided: sorted.filter((l) => l.status.trim().toLowerCase() !== PENDING),
  };
}
