// Mirrors ReportSetupDtos.cs (Examination / Report Card, stage 1: setup). Dates are yyyy-MM-dd strings.

export type EvaluationMode = 'MARKS' | 'GRADES';
export type ScaleKind = 'PERCENT_BANDS' | 'RATING';
export type EntryMode = 'MARKS' | 'GRADE';
export type AreaCategory = 'CO_SCHOLASTIC' | 'DISCIPLINE' | 'OTHER';

// ---------------------------------------------------------------- grade scales
export interface GradeBand {
  sequence: number;
  gradeCode: string;
  label: string;
  /** PERCENT_BANDS: the LOWER edge, inclusive. Null for a rating. */
  minPercent?: number | null;
  /** The derived upper edge for display (the next band's lower edge; 100 for the top band). */
  upToPercent?: number | null;
  gradePoint?: number | null;
  isFail: boolean;
  description?: string | null;
}

export interface GradeScaleListItem {
  gradeScaleId: number;
  scaleName: string;
  kind: ScaleKind;
  sourcePresetCode?: string | null;
  bandCount: number;
  schemeCount: number;
  areaCount: number;
  statusName: string;
}

export interface GradeScale extends GradeScaleListItem {
  /** Highest band first for percent scales. */
  bands: GradeBand[];
}

export interface ScalePreset {
  code: string;
  name: string;
  kind: ScaleKind;
  note: string;
  bands: GradeBand[];
}

export interface BandRequest {
  gradeCode: string;
  label: string;
  minPercent: number | null;
  gradePoint?: number | null;
  isFail: boolean;
  description?: string | null;
}

export interface SaveGradeScaleRequest {
  name: string;
  kind: ScaleKind;
  bands: BandRequest[];
}

// ---------------------------------------------------------------- templates and presets
export interface ReportTemplate {
  key: string;
  name: string;
  description: string;
  modes: EvaluationMode[];
  orientation: string;
  implemented: boolean;
}

export interface PresetExam { name: string; maxMarks: number; weight: number }
export interface PresetTerm { name: string; exams: PresetExam[] }

export interface SchemePreset {
  code: string;
  name: string;
  description: string;
  evaluationMode: EvaluationMode;
  templateKey: string;
  scalePresetCode: string;
  scaleName: string;
  showRank: boolean;
  entryMode: EntryMode;
  terms: PresetTerm[];
  skills: string[];
}

// ---------------------------------------------------------------- schemes
export interface SchemeListItem {
  schemeId: number;
  schemeName: string;
  academicYearId: number;
  academicYearName: string;
  evaluationMode: EvaluationMode;
  templateKey: string;
  classNames: string[];
  termCount: number;
  examCount: number;
  subjectCount: number;
  statusName: string;
}

export interface SchemeClass { classId: number; className: string }
export interface SchemeExam { examId: number; name: string; sequence: number; defaultMaxMarks: number; weightPoints: number; examDate?: string | null }
export interface SchemeTerm { termId: number; name: string; sequence: number; startDate: string; endDate: string; yearWeight?: number | null; exams: SchemeExam[] }
export interface SchemeSkill { skillId: number; name: string; sequence: number }

export interface SchemeSubject {
  schemeSubjectId: number;
  classId: number;
  className: string;
  subjectId: number;
  subjectName: string;
  displayOrder: number;
  labelOverride?: string | null;
  includeInTotal: boolean;
  skills: SchemeSkill[];
}

export interface SchemeCell {
  examSubjectId: number;
  examId: number;
  schemeSubjectId: number;
  classId: number;
  maxMarks?: number | null;
  weightPoints?: number | null;
  entryMode: EntryMode;
  effectiveMax?: number | null;
  effectiveWeight?: number | null;
}

export interface Scheme extends SchemeListItem {
  gradeScaleId?: number | null;
  gradeScaleName?: string | null;
  yearCombine: 'SUM_OF_TERMS' | 'WEIGHTED';
  subjectPassPercent: number;
  overallPassPercent: number;
  showRank: boolean;
  rankScope: 'SECTION' | 'CLASS';
  attendanceSource: 'AUTO' | 'MANUAL';
  percentageRounding: 'ROUND_HALF_UP' | 'TRUNCATE';
  classes: SchemeClass[];
  terms: SchemeTerm[];
  subjects: SchemeSubject[];
  cells: SchemeCell[];
  warnings: string[];
}

export interface SaveSchemeRequest {
  name: string;
  academicYearId: number;
  evaluationMode: EvaluationMode;
  gradeScaleId: number | null;
  yearCombine: string;
  subjectPassPercent: number;
  overallPassPercent: number;
  showRank: boolean;
  rankScope: string;
  attendanceSource: string;
  percentageRounding: string;
  templateKey: string;
}

export interface SaveExamRequest { examId?: number | null; name: string; defaultMaxMarks: number; weightPoints?: number | null }
export interface SaveTermRequest { termId?: number | null; name: string; startDate: string; endDate: string; yearWeight?: number | null; exams: SaveExamRequest[] }
export interface SaveSchemeSubjectRequest { classId: number; subjectId: number; displayOrder: number; labelOverride?: string | null; includeInTotal: boolean }
export interface SaveCellRequest { examId: number; schemeSubjectId: number; maxMarks?: number | null; weightPoints?: number | null; entryMode: EntryMode }
export interface SaveSubjectSkillsRequest { schemeSubjectId: number; skills: string[] }

export interface SchemeFromPresetRequest {
  presetCode: string;
  academicYearId: number;
  schemeName?: string | null;
  classIds: number[];
  termDates?: { startDate: string; endDate: string }[];
  templateKey?: string | null;
  showRank?: boolean | null;
  percentageRounding?: string | null;
  subjectPassPercent?: number | null;
  overallPassPercent?: number | null;
}

// ---------------------------------------------------------------- activity areas
export interface ActivityArea {
  areaId: number;
  category: AreaCategory;
  name: string;
  sequence: number;
  ratingScaleId: number;
  ratingScaleName: string;
  statusName: string;
}

export interface SaveActivityAreaRequest { category: AreaCategory; name: string; sequence: number; ratingScaleId: number }
export interface StarterAreasResult { created: number; alreadyThere: number; areas: ActivityArea[] }

// ---------------------------------------------------------------- exam schedule
export interface ScheduleClassDate { classId: number; className: string; examDate: string }
export interface ScheduleExam { examId: number; termName: string; examName: string; examDate?: string | null; classDates: ScheduleClassDate[] }
export interface ScheduleScheme { schemeId: number; schemeName: string; academicYearName: string; classNames: string[]; classes: SchemeClass[]; exams: ScheduleExam[] }
export interface SaveScheduleRequest {
  schemeId: number;
  exams: { examId: number; examDate: string | null; classDates: { classId: number; examDate: string }[] }[];
}

// ---------------------------------------------------------------- display helpers (pure, so they are easy to test)
export const MODE_LABEL: Record<EvaluationMode, string> = { MARKS: 'Marks', GRADES: 'Grades only' };
export const CATEGORY_LABEL: Record<AreaCategory, string> = { CO_SCHOLASTIC: 'Co-scholastic', DISCIPLINE: 'Discipline', OTHER: 'Other' };

/** The same even split of an academic year into terms that the server uses by default. */
export function splitYear(start: string, end: string, count: number): { startDate: string; endDate: string }[] {
  const s = Date.UTC(+start.slice(0, 4), +start.slice(5, 7) - 1, +start.slice(8, 10));
  const e = Date.UTC(+end.slice(0, 4), +end.slice(5, 7) - 1, +end.slice(8, 10));
  const days = Math.round((e - s) / 86_400_000) + 1;
  const iso = (ms: number) => new Date(ms).toISOString().slice(0, 10);
  return Array.from({ length: count }, (_, i) => ({
    startDate: iso(s + Math.floor((days * i) / count) * 86_400_000),
    endDate: i === count - 1 ? end : iso(s + (Math.floor((days * (i + 1)) / count) - 1) * 86_400_000),
  }));
}
