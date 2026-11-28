import { api } from './index';

// Typed endpoints for the PARENT (Guardian) app. EVERY call is a GET on /api/Family and carries no identity: the guardian and the school come from the token, and the
// child id in the path is checked by the server against the guardian's links (a child that is not this parent's is a 404). Nothing here is ever a write, and nothing
// outside /api/Family is called. Shapes follow smss-api's FamilyDtos (camelCase JSON; dates "yyyy-MM-dd", times "HH:mm:ss").

export type ChipState = 'PASSED_OUT' | 'LEFT' | 'TRANSFERRED_OUT' | 'PENDING_PROMOTION';

export interface FamilyChild {
  studentId: string;
  fullName: string;
  admissionNumber: string;
  statusName: string;
  academicYearName: string | null;
  className: string | null;
  sectionName: string | null;
  rollNumber: string | null;
  /** Read-only history (passed out, left, transferred) or waiting to be promoted; null for a current student. */
  chipState: ChipState | null;
  readOnly: boolean;
  /** "Present" / "Absent", or null when today is not marked (or the child is not current). */
  attendanceToday: string | null;
  feeDueNow: number;
  /** Unpaid from years before the one shown for this child (derived; absent on an older server). */
  feeEarlierYears?: number;
}

export interface FamilyFeeTile {
  allPaid: boolean;
  totalDueNow: number;
  totalEarlierYears?: number;
  children: { studentId: string; fullName: string; dueNow: number; earlierYears?: number }[];
}

export interface FamilyChildren {
  children: FamilyChild[];
  feeTile: FamilyFeeTile;
}

export interface FamilyProfile {
  studentId: string;
  fullName: string;
  admissionNumber: string;
  statusName: string;
  academicYearName: string | null;
  className: string | null;
  sectionName: string | null;
  rollNumber: string | null;
  genderName: string | null;
  dateOfBirth: string;
  bloodGroupName: string | null;
  address: string | null;
}

export interface FamilyAttendance {
  from: string;
  to: string;
  summary: { markedDays: number; byStatus: Record<string, number>; presentPercentage: number | null };
  days: { date: string; statusCode: string; statusName: string }[];
}

/** No attachment fields: parents do not see homework attachments yet. */
export interface FamilyHomework {
  homeworkId: number;
  subjectName: string;
  teacherName: string;
  title: string;
  description: string | null;
  assignedDate: string;
  dueDate: string;
}

export interface FamilyTimetable {
  days: { dayOfWeek: number; name: string }[];
  rows: { period: { periodId: number; periodNumber: number; startTime: string; endTime: string; slotType?: string }; cells: { dayOfWeek: number; entry: { subjectName: string; employeeName: string | null; roomNo: string | null } | null }[] }[];
  entryCount: number;
}

export interface FamilyExamSchedule {
  schemeName: string;
  academicYearName: string;
  exams: { termName: string; examName: string; examDate: string | null }[];
}

export interface FamilyReportCard {
  publicationId: number;
  schemeName: string;
  scope: string;
  termName: string | null;
  publishedAt: string;
  percent: number | null;
  grade: string | null;
  result: string;
}

export interface FamilyFees {
  hasFees: boolean;
  overallStatus: string;
  totalDue: number;
  totalPaid: number;
  balance: number;
  dues: { feeTypeName: string; dueDate: string; amountDue: number; amountPaid: number; balance: number; status: string }[];
  /** "Outstanding from earlier years" (derived; nothing is carried forward). Absent on an older server. */
  earlierYears?: { academicYearId: number; academicYearName: string; outstanding: number }[];
  earlierYearsOutstanding?: number;
  earlierDues?: { feeTypeName: string; dueDate: string; balance: number; status: string; academicYearName?: string | null }[];
  payments: {
    paymentId?: number;
    paymentDate: string;
    amount: number;
    receiptNumber: string;
    paymentMode: string;
    lateFee?: { decision: string; amount: number; ruleText?: string | null } | null;
  }[];
}

export interface FamilyReceipt {
  paymentId: number;
  receiptNumber: string;
  paymentDate: string;
  /** What was handed over (fee + late fee + tax). */
  amount: number;
  paymentMode: string;
  school: { name: string };
  student: { studentId: string; name: string; className?: string | null };
  lines: { feeTypeName: string; dueDate: string; amountDue: number; allocatedAmount: number }[];
  lateFee?: { decision: string; amount: number; ruleText?: string | null } | null;
  taxAmount?: number;
}

export interface FamilyLeave {
  leaveRequestId: number;
  leaveTypeName: string;
  fromDate: string;
  toDate: string;
  days: number;
  reason: string;
  status: string;
}

export interface FamilyNotice {
  noticeId: number;
  title: string;
  body: string;
  publishedAt: string;
  isRead: boolean;
  postedBy: string | null;
}

export interface FamilyEvent {
  eventId: number;
  title: string;
  startDate: string;
  endDate: string;
  isAllDay: boolean;
  startTime: string | null;
  endTime: string | null;
  categoryName: string;
  colorHex: string;
  description: string | null;
}

const base = (studentId: string) => `/api/Family/children/${encodeURIComponent(studentId)}`;

export const getChildren = () => api.request<FamilyChildren>('/api/Family/children');
export const getProfile = (id: string) => api.request<FamilyProfile>(`${base(id)}/profile`);
export const getAttendance = (id: string, from: string, to: string) => api.request<FamilyAttendance>(`${base(id)}/attendance`, { query: { from, to } });
export const getHomework = (id: string) => api.request<FamilyHomework[]>(`${base(id)}/homework`);
export const getTimetable = (id: string) => api.request<FamilyTimetable>(`${base(id)}/timetable`);
export const getExamSchedule = (id: string) => api.request<FamilyExamSchedule[]>(`${base(id)}/exam-schedule`);
export const getReportCards = (id: string) => api.request<FamilyReportCard[]>(`${base(id)}/report-cards`);
export const reportCardPdfPath = (id: string, publicationId: number) => `${base(id)}/report-cards/${publicationId}/pdf`;
export const getFees = (id: string) => api.request<FamilyFees>(`${base(id)}/fees`);
export const getReceipt = (id: string, paymentId: number) => api.request<FamilyReceipt>(`${base(id)}/receipts/${paymentId}`);
export const getLeave = (id: string) => api.request<FamilyLeave[]>(`${base(id)}/leave`);

export const getNotices = () => api.request<FamilyNotice[]>('/api/Family/notices');
export const getUnreadNotices = async () => (await api.request<{ count: number }>('/api/Family/notices/unread-count')).count;
/** Opening a notice marks it read: the API does that on this GET. There is deliberately NO separate mark-read call. */
export const getNotice = (noticeId: number) => api.request<FamilyNotice>(`/api/Family/notices/${noticeId}`);
export const getCalendar = (from: string, to: string) => api.request<FamilyEvent[]>('/api/Family/calendar', { query: { from, to } });
