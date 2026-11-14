import { api } from './index';

// Typed endpoint module for the STUDENT app. Shapes follow smss-api's DTOs (camelCase JSON; dates "yyyy-MM-dd", times "HH:mm:ss").
// The school and the student always come from the token: nothing here sends a school id or a student id.

export interface DashboardProfile {
  studentId: string;
  fullName: string;
  admissionNumber: string;
  className: string | null;
  sectionName: string | null;
  rollNumber: string | null;
  academicYearName: string | null;
}

export interface DashboardAttendance {
  presentCount: number;
  absentCount: number;
  totalMarkedDays: number;
  presentPercentage: number | null;
  from: string;
  to: string;
}

export interface TodayClass {
  periodNumber: number;
  subjectName: string;
  teacherName: string;
  roomNo: string | null;
  startTime: string;
  endTime: string;
  /** Completed / InProgress / Upcoming: computed by the server. */
  status: string;
}

export interface Notice {
  noticeId: number;
  title: string;
  body: string;
  audience: string;
  createdByName: string | null;
  publishedAt: string;
  isRead: boolean;
}

/** Pending / Approved / Rejected / Cancelled: derived by the server, never recomputed here. */
export interface StudentLeave {
  leaveRequestId: number;
  leaveTypeName: string;
  fromDate: string;
  toDate: string;
  days: number;
  reason: string;
  status: string;
  appliedAt: string;
  decisionReason: string | null;
}

export interface StudentDashboard {
  profile: DashboardProfile;
  /** null when that part could not be loaded on the server. */
  attendance: DashboardAttendance | null;
  unreadNoticesCount: number;
  /** The SERVER's date. Every "today" decision in the app uses this, never the phone's clock. */
  today: string;
  todayClasses: TodayClass[];
  recentLeave: StudentLeave[];
}

export const getDashboard = () => api.request<StudentDashboard>('/api/StudentDashboard');

export interface Homework {
  homeworkId: number;
  subjectName: string;
  teacherName: string;
  title: string;
  description: string | null;
  assignedDate: string;
  dueDate: string;
  hasAttachment: boolean;
  attachmentFileName: string | null;
}

export const getHomework = () => api.request<Homework[]>('/api/Homework/student/my');

export interface MarkResult {
  assessmentId: number;
  title: string;
  assessmentTypeName: string;
  subjectName: string;
  testDate: string;
  maxMarks: number;
  /** null when the student was absent. */
  obtainedMarks: number | null;
  isAbsent: boolean;
}

export const getMarks = () => api.request<MarkResult[]>('/api/Assessment/student/my');

export interface TimetableEntry {
  subjectName: string;
  employeeName: string;
  roomNo: string | null;
}
export interface TimetableRow {
  period: { periodId: number; periodNumber: number; startTime: string; endTime: string };
  cells: { dayOfWeek: number; entry: TimetableEntry | null }[];
}
export interface TimetableGrid {
  days: { dayOfWeek: number; name: string }[];
  rows: TimetableRow[];
  entryCount: number;
}

export const getTimetable = () => api.request<TimetableGrid>('/api/Timetable/student/my');

export const getNotices = () => api.request<Notice[]>('/api/Notice/my');
export const getUnreadCount = async () => (await api.request<{ count: number }>('/api/Notice/unread-count')).count;
/** Opening a notice marks it read: the API does that on this GET. There is deliberately NO separate mark-read call. */
export const getNotice = (id: number) => api.request<Notice>(`/api/Notice/${id}`);

export interface AttendanceDay {
  date: string;
  statusCode: string;
  statusName: string;
}
export interface OwnAttendance {
  from: string;
  to: string;
  days: AttendanceDay[];
  summary: { markedDays: number; byStatus: Record<string, number>; presentPercentage: number | null };
}

// GET /api/Attendance/student/my: at most 92 days per request; a calendar month is always within that.
export const getOwnAttendance = (from: string, to: string) => api.request<OwnAttendance>('/api/Attendance/student/my', { query: { from, to } });

// ---- "Where is my bus": GET /api/Transport/my-bus (Student role only). The server answers with ONLY these fields: no phone, photo, address or licence, and no
// coordinates for an OFFLINE bus. assigned:false is a normal 200.
export type BusStatus = 'LIVE' | 'STALE' | 'OFFLINE';

export interface MyBusItem {
  driverName: string;
  vehicleLabel: string;
  vehiclePlate: string;
  status: BusStatus;
  latitude: number | null;
  longitude: number | null;
  /** Seconds since the last location arrived (LIVE and STALE only). */
  ageSeconds: number | null;
}

export interface MyBus {
  assigned: boolean;
  area: { areaName: string } | null;
  buses: MyBusItem[];
  serverTime: string;
  message: string | null;
}

export const getMyBus = () => api.request<MyBus>('/api/Transport/my-bus');
