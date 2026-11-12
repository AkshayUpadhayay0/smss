import { api } from './index';

// Typed endpoint module for the TEACHER app. The school and the teacher always come from the token: nothing here sends a school id or an employee id
// (the API infers the owner of homework / tests from the token and refuses any other).

export interface TeacherSection {
  classId: number;
  className: string;
  sectionId: number;
  sectionName: string;
}

/** Parts the server could not load are listed in partsUnavailable: the matching tile is hidden, never shown as 0. */
export interface TeacherDashboard {
  today: string;
  profile: { employeeId: string; fullName: string; designationName: string; departmentName: string };
  classTeacherOf: TeacherSection[];
  subjectsTaught: string[];
  totalStudents: number | null;
  feePending: number | null;
  presentToday: number | null;
  absentToday: number | null;
  notMarkedYet: boolean;
  unmarkedSections: number;
  partsUnavailable: string[];
}

export const getTeacherDashboard = () => api.request<TeacherDashboard>('/api/TeacherDashboard');

// ---- the teacher's own weekly timetable: the source of the section / subject pickers and of "my periods today"
export interface TeacherEntry {
  academicYearId: number;
  classId: number;
  className: string;
  sectionId: number;
  sectionName: string;
  subjectId: number;
  subjectName: string;
  roomNo: string | null;
}
export interface TeacherGridRow {
  period: { periodId: number; periodNumber: number; startTime: string; endTime: string };
  cells: { dayOfWeek: number; entry: TeacherEntry | null }[];
}
export interface TeacherGrid {
  academicYearId: number;
  academicYearName: string;
  rows: TeacherGridRow[];
}

export const getMyTimetable = () => api.request<TeacherGrid>('/api/Timetable/my');

// ---- attendance
export interface MarkableSection extends TeacherSection {
  rosterCount: number;
  markedToday: boolean;
}
export interface MySections {
  today: string;
  academicYearId: number;
  academicYearName: string;
  sections: MarkableSection[];
}
export const getMySections = () => api.request<MySections>('/api/Attendance/my-sections');

export interface AttendanceStatus {
  attendanceStatusId: number;
  statusCode: string;
  statusName: string;
}
export interface SectionStudent {
  studentId: string;
  fullName: string;
  rollNumber: string | null;
  attendanceStatusId: number | null;
  statusCode: string | null;
  onRoster: boolean;
}
export interface SectionAttendance {
  sectionId: number;
  className: string;
  sectionName: string;
  academicYearId: number;
  /** The SERVER's date for this roster; the submit must be for exactly this date. */
  date: string;
  isToday: boolean;
  rosterCount: number;
  markedCount: number;
  isMarked: boolean;
  canSubmit: boolean;
  statuses: AttendanceStatus[];
  students: SectionStudent[];
}
export const getSectionAttendance = (sectionId: number) => api.request<SectionAttendance>(`/api/Attendance/section/${sectionId}`);

export interface SubmitAttendanceResult {
  total: number;
  present: number;
  absent: number;
}
export const submitAttendance = (body: { sectionId: number; date: string; records: { studentId: string; attendanceStatusId: number }[] }) =>
  api.request<SubmitAttendanceResult>('/api/Attendance/submit', { method: 'POST', body });

// ---- homework
export interface MyHomework {
  homeworkId: number;
  academicYearId: number;
  className: string;
  sectionId: number;
  sectionName: string;
  subjectId: number;
  subjectName: string;
  title: string;
  description: string | null;
  assignedDate: string;
  dueDate: string;
  hasAttachment: boolean;
  attachmentFileName: string | null;
}
export const getMyHomework = () => api.request<MyHomework[]>('/api/Homework/my');

export interface PublishHomeworkBody {
  sectionId: number;
  subjectId: number;
  title: string;
  description?: string;
  dueDate: string;
}
export const publishHomework = (body: PublishHomeworkBody) => api.request<MyHomework>('/api/Homework', { method: 'POST', body });

export interface EditHomeworkBody {
  title: string;
  description?: string;
  assignedDate: string;
  dueDate: string;
}
export const updateHomework = (id: number, body: EditHomeworkBody) => api.request<MyHomework>(`/api/Homework/${id}`, { method: 'PUT', body });
/** Soft delete on the server (the row is hidden, never removed). */
export const deleteHomework = (id: number) => api.request<unknown>(`/api/Homework/${id}`, { method: 'DELETE' });

export interface PickedFile {
  uri: string;
  name: string;
  mimeType: string;
}
/** Multipart upload; one attachment per homework (a new one replaces it). */
export function uploadHomeworkAttachment(homeworkId: number, file: PickedFile) {
  const form = new FormData();
  // React Native's FormData file part: { uri, name, type }. The client sends multipart through XMLHttpRequest, which reads the file natively
  // (see sendMultipart in client.ts). The file name is explicit: the server validates the extension.
  form.append('file', { uri: file.uri, name: file.name, type: file.mimeType } as unknown as Blob);
  return api.request<MyHomework>(`/api/Homework/${homeworkId}/attachment`, { method: 'POST', formData: form });
}

// ---- tests / marks
export interface AssessmentType {
  assessmentTypeId: number;
  typeName: string;
  typeCode: string;
}
export const getAssessmentTypes = () => api.request<AssessmentType[]>('/api/Assessment/types');

export interface RosterStudent {
  studentId: string;
  fullName: string;
  rollNumber: string | null;
}
export const getAssessmentRoster = (academicYearId: number, sectionId: number, subjectId: number) =>
  api.request<RosterStudent[]>('/api/Assessment/roster', { query: { academicYearId, sectionId, subjectId } });

export interface CreateAssessmentBody {
  academicYearId: number;
  sectionId: number;
  subjectId: number;
  assessmentTypeId: number;
  title: string;
  testDate: string;
  maxMarks: number;
  marks: { studentId: string; obtainedMarks?: number; isAbsent?: boolean }[];
}
export const createAssessment = (body: CreateAssessmentBody) => api.request<{ assessmentId: number }>('/api/Assessment', { method: 'POST', body });

// ---- student leave (a class teacher decides their own section's requests)
export interface StudentLeaveRequest {
  leaveRequestId: number;
  leaveTypeName: string;
  fromDate: string;
  toDate: string;
  days: number;
  reason: string;
  /** Pending / Approved / Rejected / Cancelled: derived by the server. */
  status: string;
  decidedByName: string | null;
  decisionReason: string | null;
  studentId: string;
  studentName: string;
  className: string | null;
  sectionName: string | null;
}
// No status filter: Pending vs Decided is split client-side (one request, and both tabs stay consistent).
export const getStudentLeave = () => api.request<StudentLeaveRequest[]>('/api/StudentLeave');
export const decideLeave = (id: number, body: { decision: 'Approve' | 'Reject'; decisionReason?: string }) =>
  api.request<StudentLeaveRequest>(`/api/StudentLeave/${id}/decide`, { method: 'PUT', body });
