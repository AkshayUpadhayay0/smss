// Mirrors ReportMarksDtos.cs (Examination / Report Card, stage 2: marks entry and the class teacher's inputs).

export type EntryState = 'ENTERED' | 'ABSENT' | 'EXEMPT';
export type GridStatus = 'DRAFT' | 'SUBMITTED' | 'ACCEPTED';
export type CellStatus = GridStatus | 'NOT_STARTED';

export interface MarksContextRow {
  schemeId: number; schemeName: string; evaluationMode: string;
  termId: number; termName: string; examId: number; examName: string;
  examSubjectId: number; subjectId: number; subjectName: string;
  classId: number; className: string; sectionId: number; sectionName: string;
  entryMode: 'MARKS' | 'GRADE'; maxMarks?: number | null; skillCount: number;
  status: CellStatus; locked: boolean; canEnter: boolean; canReview: boolean;
}
export interface MarksContext { academicYearId: number; academicYearName: string; rows: MarksContextRow[] }

export interface GradeOption { code: string; label: string }
export interface GridSkill { skillId: number; name: string }
export interface GridCell { skillId?: number | null; state?: EntryState | null; marks?: number | null; gradeCode?: string | null }
export interface GridStudent { studentId: string; admissionNumber: string; rollNumber: string; fullName: string; onRoster: boolean; cells: GridCell[] }
export interface GridCounts { students: number; cells: number; entered: number; absent: number; exempt: number; missing: number }

export interface MarksGrid {
  examSubjectId: number; sectionId: number; schemeId: number; schemeName: string; termName: string; examName: string; subjectName: string; className: string; sectionName: string;
  entryMode: 'MARKS' | 'GRADE'; maxMarks?: number | null; skills: GridSkill[]; gradeOptions: GradeOption[];
  status: GridStatus; version: number; locked: boolean; canEdit: boolean; canSubmit: boolean; canAccept: boolean; canReopen: boolean;
  readOnlyReason?: string | null; submittedAt?: string | null; acceptedAt?: string | null; counts: GridCounts; students: GridStudent[];
}

/** One entry the screen sends. NONE clears what was recorded. */
export interface GridEntryRequest { studentId: string; skillId?: number | null; state: EntryState | 'NONE'; marks?: number | null; gradeCode?: string | null }
export interface UploadIssue { row: number; message: string }
export interface UploadPreview { applied: boolean; rows: number; cells: number; issues: UploadIssue[]; entries: GridEntryRequest[]; grid?: MarksGrid | null }

export interface CompletenessCell { examId: number; examSubjectId: number; entryMode: string; status: CellStatus; expected: number; entered: number; absent: number; exempt: number; missing: number }
export interface CompletenessSubject { schemeSubjectId: number; subjectName: string; cells: CompletenessCell[] }
export interface CompletenessTerm {
  termId: number; termName: string; startDate: string; endDate: string; locked: boolean; lockedAt?: string | null; lastUnlockReason?: string | null;
  cells: number; accepted: number; submitted: number; draft: number; notStarted: number; readyToLock: boolean;
  exams: { examId: number; name: string }[]; subjects: CompletenessSubject[];
}
export interface Completeness {
  schemeId: number; schemeName: string; evaluationMode: string; attendanceSource: string; sectionId: number; classId: number; className: string; sectionName: string;
  rosterCount: number; canEdit: boolean; canLock: boolean; terms: CompletenessTerm[];
}

export interface ActivityArea { areaId: number; category: string; name: string; options: GradeOption[] }
export interface ActivityStudent { studentId: string; rollNumber: string; fullName: string; grades: Record<string, string> }
export interface ActivityGrades { termId: number; termName: string; sectionId: number; locked: boolean; canEdit: boolean; readOnlyReason?: string | null; areas: ActivityArea[]; students: ActivityStudent[] }

export interface TermStudent { studentId: string; rollNumber: string; fullName: string; remark?: string | null; conduct?: string | null; rankText?: string | null; manualWorkingDays?: number | null; manualPresentDays?: number | null }
export interface TermStudents { termId: number; termName: string; sectionId: number; attendanceSource: string; evaluationMode: string; locked: boolean; canEdit: boolean; readOnlyReason?: string | null; students: TermStudent[] }

export interface YearStudent { studentId: string; rollNumber: string; fullName: string; promotionDecision?: string | null; promotedTo?: string | null; newSessionDate?: string | null }
export interface YearStudents { schemeId: number; sectionId: number; locked: boolean; canEdit: boolean; readOnlyReason?: string | null; students: YearStudent[] }

export interface TermAttendanceStudent { studentId: string; rollNumber: string; fullName: string; workingDays?: number | null; presentDays?: number | null; absentDays: number; notMarkedDays: number; percentage?: number | null }
export interface TermAttendance {
  termId: number; termName: string; sectionId: number; from: string; to: string; today: string; source: string; workingDays: number; unmarkedDays: number; unmarkedDates: string[];
  warning?: string | null; students: TermAttendanceStudent[];
}
export interface LockResult { termId: number; sectionId: number; locked: boolean; lockedAt?: string | null }

// ---------------------------------------------------------------- display helpers (pure, so they are easy to test)
export const STATUS_LABEL: Record<CellStatus, string> = { NOT_STARTED: 'Not started', DRAFT: 'Draft', SUBMITTED: 'Submitted', ACCEPTED: 'Accepted' };
export const DECISIONS: { value: string; label: string }[] = [{ value: 'PROMOTED', label: 'Promoted' }, { value: 'NOT_PROMOTED', label: 'Not promoted' }, { value: 'DETAINED', label: 'Detained' }];

/** The key of one cell in the screen's local edits. */
export const cellKey = (studentId: string, skillId?: number | null): string => `${studentId}|${skillId ?? ''}`;

/** What the screen shows and counts for a cell: the saved value with the unsaved edit laid over it. */
export interface CellEdit { state: EntryState | 'NONE' | null; marks: string; gradeCode: string }

/** Live "x of y entered": every cell of every roster student that has a state (marks / grade / absent / exempt), counting unsaved edits. */
export function enteredCount(grid: MarksGrid, edits: Record<string, CellEdit>): { done: number; total: number } {
  let done = 0; let total = 0;
  for (const s of grid.students.filter((x) => x.onRoster))
    for (const c of s.cells) {
      total++;
      const e = edits[cellKey(s.studentId, c.skillId)];
      const has = e ? e.state === 'ABSENT' || e.state === 'EXEMPT' || (e.state !== 'NONE' && (grid.entryMode === 'MARKS' ? e.marks.trim() !== '' : e.gradeCode !== '')) : !!c.state;
      if (has) done++;
    }
  return { done, total };
}

/** Turns the edits into the entries the API takes. A marks cell that was emptied is cleared (NONE). */
export function editsToEntries(grid: MarksGrid, edits: Record<string, CellEdit>): GridEntryRequest[] {
  const out: GridEntryRequest[] = [];
  for (const s of grid.students.filter((x) => x.onRoster))
    for (const c of s.cells) {
      const e = edits[cellKey(s.studentId, c.skillId)];
      if (!e) continue;
      const base = { studentId: s.studentId, ...(c.skillId != null ? { skillId: c.skillId } : {}) };
      if (e.state === 'ABSENT' || e.state === 'EXEMPT') out.push({ ...base, state: e.state });
      else if (grid.entryMode === 'MARKS') out.push(e.marks.trim() === '' ? { ...base, state: 'NONE' } : { ...base, state: 'ENTERED', marks: Number(e.marks) });
      else out.push(e.gradeCode === '' ? { ...base, state: 'NONE' } : { ...base, state: 'ENTERED', gradeCode: e.gradeCode });
    }
  return out;
}

/** The distinct choices a picker offers, in the order the context lists them. */
export function distinct<T, K>(rows: T[], key: (r: T) => K): T[] {
  const seen = new Set<K>(); const out: T[] = [];
  for (const r of rows) { const k = key(r); if (!seen.has(k)) { seen.add(k); out.push(r); } }
  return out;
}
