// Mirrors AssessmentDto / AssessmentDetailDto / CreateAssessmentRequest / StudentAssessmentResultDto (internal tests: a mark, or absent, per student).

export interface AssessmentType {
  assessmentTypeId: number;
  typeName: string;
  typeCode: string;
}

export interface Assessment {
  assessmentId: number;
  academicYearId: number;
  academicYearName: string;
  classId: number;
  className: string;
  sectionId: number;
  sectionName: string;
  subjectId: number;
  subjectName: string;
  assessmentTypeId: number;
  assessmentTypeName: string;
  employeeId: string;
  teacherName: string;
  title: string;
  /** yyyy-MM-dd */
  testDate: string;
  maxMarks: number;
  studentCount: number;
  absentCount: number;
  statusName: string;
}

export interface AssessmentMark {
  studentId: string;
  fullName: string;
  rollNumber?: string | null;
  obtainedMarks?: number | null;
  isAbsent: boolean;
  /** False for a student who has since left the section: their stored result stays but is no longer editable. */
  onRoster: boolean;
}

export interface AssessmentDetail extends Assessment {
  marks: AssessmentMark[];
}

export interface RosterStudent {
  studentId: string;
  fullName: string;
  rollNumber?: string | null;
}

export interface AssessmentMarkInput {
  studentId: string;
  obtainedMarks: number | null;
  isAbsent: boolean;
}

export interface CreateAssessmentRequest {
  academicYearId: number;
  sectionId: number;
  subjectId: number;
  assessmentTypeId: number;
  /** Only a School Admin names the owning teacher; a teacher always owns their own. */
  employeeId?: string;
  title: string;
  testDate: string;
  maxMarks: number;
  marks: AssessmentMarkInput[];
}

export interface UpdateAssessmentRequest {
  title: string;
  testDate: string;
  maxMarks: number;
  marks: AssessmentMarkInput[];
}

export interface AssessmentFilters {
  academicYearId?: number;
  sectionId?: number;
  subjectId?: number;
  /** Dates filter on the test date, inclusive. */
  from?: string;
  to?: string;
}

/** A student's own result: nothing about anyone else. */
export interface StudentResult {
  assessmentId: number;
  title: string;
  assessmentTypeName: string;
  subjectName: string;
  testDate: string;
  maxMarks: number;
  obtainedMarks?: number | null;
  isAbsent: boolean;
}

/** A row of the teacher / admin table: display-ready values. */
export interface AssessmentRow extends Record<string, unknown> {
  assessmentId: number;
  title: string;
  typeName: string;
  group: string;
  subjectName: string;
  teacherName: string;
  date: string;
  maxMarks: number;
  entered: string;
}

export const MAX_MARKS_LIMIT = 9999.99;
