// Core domain models for the School Management System theme.
// These interfaces are the contract that mock services fulfil today
// and that real HTTP services can fulfil tomorrow.

export type Gender = 'Male' | 'Female' | 'Other';
export type StudentStatus = 'Active' | 'Inactive' | 'Alumni' | 'Suspended';
export type StaffStatus = 'Active' | 'On Leave' | 'Inactive';
export type AttendanceStatus = 'Present' | 'Absent' | 'Late' | 'Leave';
export type FeeStatus = 'Paid' | 'Pending' | 'Overdue' | 'Partial';
export type HomeworkStatus = 'Assigned' | 'Submitted' | 'Pending' | 'Overdue' | 'Graded';
export type AdmissionStatus = 'Pending' | 'Under Review' | 'Approved' | 'Rejected' | 'Waiting List';
export type NoticeAudience = 'All' | 'Students' | 'Parents' | 'Teachers' | 'Staff' | 'Specific Class';
export type EventType = 'Holiday' | 'Exam' | 'Meeting' | 'Function' | 'Sports' | 'Workshop' | 'Birthday' | 'Other';

export interface Address {
  line1: string;
  city: string;
  state: string;
  pincode: string;
  country: string;
}

export interface Guardian {
  name: string;
  relation: string;
  phone: string;
  email: string;
  occupation?: string;
}

export interface Student {
  id: string;
  admissionNo: string;
  rollNo: string;
  firstName: string;
  lastName: string;
  photo: string;
  gender: Gender;
  dob: string;
  classId: string;
  className: string;
  sectionId: string;
  sectionName: string;
  status: StudentStatus;
  guardian: Guardian;
  phone: string;
  email: string;
  address: Address;
  bloodGroup: string;
  admissionDate: string;
  attendancePercent: number;
  feeStatus: FeeStatus;
  documents: { name: string; type: string; uploadedOn: string }[];
}

export interface Teacher {
  id: string;
  employeeId: string;
  firstName: string;
  lastName: string;
  photo: string;
  gender: Gender;
  department: string;
  designation: string;
  subjects: string[];
  classesAssigned: string[];
  phone: string;
  email: string;
  address: Address;
  joiningDate: string;
  status: StaffStatus;
  qualification: string;
  experienceYears: number;
}

export interface SchoolClass {
  id: string;
  name: string;
  sections: Section[];
  classTeacherId?: string;
  studentCount: number;
}

export interface Section {
  id: string;
  name: string;
  classId: string;
  studentCount: number;
  classTeacherId?: string;
  roomNo?: string;
}

export interface Subject {
  id: string;
  name: string;
  code: string;
  type: 'Core' | 'Elective' | 'Extra-curricular';
  classIds: string[];
  teacherIds: string[];
}

export interface AcademicSession {
  id: string;
  label: string;
  startDate: string;
  endDate: string;
  status: 'Active' | 'Upcoming' | 'Archived';
}

export interface AttendanceRecord {
  id: string;
  studentId: string;
  studentName: string;
  classId: string;
  sectionId: string;
  date: string;
  status: AttendanceStatus;
  remark?: string;
}

export interface TeacherAttendanceRecord {
  id: string;
  teacherId: string;
  teacherName: string;
  date: string;
  status: AttendanceStatus;
  checkIn?: string;
  checkOut?: string;
}

export interface TimetablePeriod {
  day: 'Mon' | 'Tue' | 'Wed' | 'Thu' | 'Fri' | 'Sat';
  startTime: string;
  endTime: string;
  subject: string;
  teacher: string;
  room: string;
  isBreak?: boolean;
}

export interface ClassTimetable {
  classId: string;
  sectionId: string;
  periods: TimetablePeriod[];
}

export interface Homework {
  id: string;
  title: string;
  description: string;
  subject: string;
  classId: string;
  sectionId: string;
  teacherName: string;
  assignedDate: string;
  dueDate: string;
  submissions: number;
  totalStudents: number;
  status: HomeworkStatus;
  attachments: string[];
}

export type ExamType = 'Unit Test' | 'Mid Term' | 'Final Examination' | 'Pre-Board';

export interface Exam {
  id: string;
  name: string;
  type: ExamType;
  classId: string;
  startDate: string;
  endDate: string;
  status: 'Upcoming' | 'Ongoing' | 'Completed';
  subjects: { subject: string; date: string; maxMarks: number }[];
}

export interface MarksEntry {
  studentId: string;
  studentName: string;
  rollNo: string;
  marks: Record<string, number>;
  total: number;
  percentage: number;
  grade: string;
  result: 'Pass' | 'Fail';
}

export interface Admission {
  id: string;
  applicationId: string;
  studentName: string;
  photo: string;
  classAppliedFor: string;
  guardianName: string;
  phone: string;
  email: string;
  applicationDate: string;
  status: AdmissionStatus;
  previousSchool?: string;
}

export interface FeeStructureItem {
  head: string;
  amount: number;
  frequency: 'One-time' | 'Monthly' | 'Quarterly' | 'Annually';
}

export interface StudentFee {
  id: string;
  studentId: string;
  studentName: string;
  classSection: string;
  totalFee: number;
  paid: number;
  pending: number;
  dueDate: string;
  status: FeeStatus;
  lastPaymentDate?: string;
}

export interface Payment {
  id: string;
  receiptNo: string;
  studentId: string;
  studentName: string;
  amount: number;
  mode: 'Cash' | 'Card' | 'UPI' | 'Bank Transfer' | 'Cheque';
  date: string;
  head: string;
}

export interface Book {
  id: string;
  title: string;
  author: string;
  category: string;
  publisher: string;
  isbn: string;
  quantity: number;
  available: number;
  status: 'Available' | 'Out of Stock';
  coverColor: string;
}

export interface LibraryIssue {
  id: string;
  bookTitle: string;
  memberName: string;
  memberType: 'Student' | 'Teacher';
  issueDate: string;
  dueDate: string;
  returnDate?: string;
  status: 'Issued' | 'Returned' | 'Overdue';
  fine?: number;
}

export interface Vehicle {
  id: string;
  number: string;
  type: string;
  capacity: number;
  driverName: string;
  routeName: string;
  status: 'Active' | 'Maintenance';
}

export interface TransportRoute {
  id: string;
  name: string;
  vehicleNo: string;
  driverName: string;
  stops: string[];
  studentCount: number;
}

export interface HostelRoom {
  id: string;
  building: string;
  roomNo: string;
  capacity: number;
  occupied: number;
  type: 'Single' | 'Double' | 'Dormitory';
  students: string[];
}

export interface SchoolEvent {
  id: string;
  title: string;
  type: EventType;
  date: string;
  endDate?: string;
  time?: string;
  location?: string;
  description: string;
}

export interface Notice {
  id: string;
  title: string;
  description: string;
  audience: NoticeAudience;
  publishDate: string;
  expiryDate: string;
  status: 'Published' | 'Draft' | 'Expired';
  attachment?: string;
}

export type NotificationType = 'General' | 'Attendance' | 'Fees' | 'Homework' | 'Examination' | 'Emergency';

export interface AppNotification {
  id: string;
  title: string;
  message: string;
  type: NotificationType;
  date: string;
  read: boolean;
}

export interface Activity {
  id: string;
  actor: string;
  action: string;
  timestamp: string;
  icon: string;
}

export interface StatCard {
  label: string;
  value: string;
  change?: string;
  trend?: 'up' | 'down';
  icon: string;
  color: string;
}

export type UserRole = 'Admin' | 'Principal' | 'Teacher' | 'Student' | 'Parent' | 'Accountant' | 'Librarian' | 'Reception';

export interface CurrentUser {
  id: string;
  name: string;
  email: string;
  role: UserRole;
  avatar: string;
}
