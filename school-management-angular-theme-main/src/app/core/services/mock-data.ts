import {
  Student, Teacher, SchoolClass, Section, Subject, AcademicSession, AttendanceRecord,
  TeacherAttendanceRecord, Homework, Exam, MarksEntry, Admission, StudentFee, Payment,
  Book, LibraryIssue, Vehicle, TransportRoute, HostelRoom, SchoolEvent, Notice,
  AppNotification, Activity, ClassTimetable, TimetablePeriod
} from '../models/school.models';

// ---- Simple deterministic PRNG so demo data is stable across reloads ----
let seed = 42;
function rnd(): number {
  seed = (seed * 9301 + 49297) % 233280;
  return seed / 233280;
}
function pick<T>(arr: T[]): T {
  return arr[Math.floor(rnd() * arr.length)];
}
function randInt(min: number, max: number): number {
  return Math.floor(rnd() * (max - min + 1)) + min;
}

const FIRST_NAMES_M = ['Aarav', 'Vihaan', 'Reyansh', 'Arjun', 'Sai', 'Krishna', 'Ishaan', 'Rohan', 'Kabir', 'Aditya', 'Vivaan', 'Aryan', 'Dhruv', 'Karthik', 'Yash', 'Rudra', 'Advait', 'Shaurya', 'Devansh', 'Om'];
const FIRST_NAMES_F = ['Ananya', 'Riya', 'Ishita', 'Diya', 'Aadhya', 'Saanvi', 'Myra', 'Anika', 'Kiara', 'Navya', 'Pari', 'Sara', 'Tara', 'Zara', 'Meera', 'Avni', 'Kavya', 'Prisha', 'Siya', 'Vanya'];
const LAST_NAMES = ['Sharma', 'Verma', 'Gupta', 'Singh', 'Kumar', 'Patel', 'Reddy', 'Rao', 'Iyer', 'Nair', 'Menon', 'Joshi', 'Malhotra', 'Kapoor', 'Chopra', 'Mehta', 'Agarwal', 'Bhatt', 'Desai', 'Pillai'];
const DEPARTMENTS = ['Mathematics', 'Science', 'English', 'Social Studies', 'Hindi', 'Computer Science', 'Physical Education', 'Arts', 'Music', 'Administration'];
const SUBJECTS_LIST = ['Mathematics', 'Science', 'English', 'Hindi', 'Social Studies', 'Computer Science', 'Physical Education', 'Art & Craft', 'Music', 'Sanskrit'];
const CLASS_NAMES = ['Nursery', 'LKG', 'UKG', '1', '2', '3', '4', '5', '6', '7', '8', '9', '10', '11', '12'];
const SECTION_NAMES = ['A', 'B', 'C', 'D'];
const AVATAR_COLORS = ['#6366f1', '#8b5cf6', '#06b6d4', '#10b981', '#f59e0b', '#ef4444', '#ec4899', '#14b8a6'];

function fullName(gender: 'Male' | 'Female'): { first: string; last: string } {
  const first = gender === 'Male' ? pick(FIRST_NAMES_M) : pick(FIRST_NAMES_F);
  return { first, last: pick(LAST_NAMES) };
}

function initialsAvatar(name: string): string {
  const parts = name.split(' ');
  const initials = (parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? '');
  const color = pick(AVATAR_COLORS);
  return `data:image/svg+xml,${encodeURIComponent(
    `<svg xmlns="http://www.w3.org/2000/svg" width="80" height="80"><rect width="80" height="80" rx="40" fill="${color}"/><text x="50%" y="54%" font-family="Inter,Arial" font-size="30" fill="white" text-anchor="middle">${initials.toUpperCase()}</text></svg>`
  )}`;
}

function pad(n: number, len = 4): string {
  return n.toString().padStart(len, '0');
}

function dateOffset(days: number): string {
  const d = new Date();
  d.setDate(d.getDate() + days);
  return d.toISOString().slice(0, 10);
}

// ---------- Sessions ----------
export const SESSIONS: AcademicSession[] = [
  { id: 'sess-2627', label: '2026–27', startDate: '2026-04-01', endDate: '2027-03-31', status: 'Active' },
  { id: 'sess-2526', label: '2025–26', startDate: '2025-04-01', endDate: '2026-03-31', status: 'Archived' },
  { id: 'sess-2425', label: '2024–25', startDate: '2024-04-01', endDate: '2025-03-31', status: 'Archived' },
];

// ---------- Classes & Sections ----------
export const CLASSES: SchoolClass[] = CLASS_NAMES.map((name, i) => {
  const numSections = name === 'Nursery' || name === 'LKG' || name === 'UKG' ? 2 : randInt(2, 4);
  const sections: Section[] = Array.from({ length: numSections }, (_, si) => ({
    id: `sec-${i}-${si}`,
    name: SECTION_NAMES[si],
    classId: `cls-${i}`,
    studentCount: randInt(28, 45),
    roomNo: `${100 + i * 10 + si}`,
  }));
  return {
    id: `cls-${i}`,
    name: `Class ${name}`,
    sections,
    studentCount: sections.reduce((a, s) => a + s.studentCount, 0),
  };
});

export const ALL_SECTIONS: Section[] = CLASSES.flatMap(c => c.sections);

// ---------- Subjects ----------
export const SUBJECTS: Subject[] = SUBJECTS_LIST.map((name, i) => ({
  id: `subj-${i}`,
  name,
  code: name.substring(0, 3).toUpperCase() + (100 + i),
  type: i < 6 ? 'Core' : 'Elective',
  classIds: CLASSES.slice(3).map(c => c.id),
  teacherIds: [],
}));

// ---------- Teachers ----------
export const TEACHERS: Teacher[] = Array.from({ length: 126 }, (_, i) => {
  const gender = rnd() > 0.45 ? 'Female' : 'Male';
  const { first, last } = fullName(gender as 'Male' | 'Female');
  const name = `${first} ${last}`;
  const dept = pick(DEPARTMENTS);
  return {
    id: `teacher-${i}`,
    employeeId: `EMP${pad(1000 + i)}`,
    firstName: first,
    lastName: last,
    photo: initialsAvatar(name),
    gender: gender as 'Male' | 'Female',
    department: dept,
    designation: pick(['PGT', 'TGT', 'PRT', 'Head of Department', 'Vice Principal', 'Sports Coach']),
    subjects: [pick(SUBJECTS_LIST), pick(SUBJECTS_LIST)].filter((v, idx, a) => a.indexOf(v) === idx),
    classesAssigned: [pick(CLASS_NAMES), pick(CLASS_NAMES)],
    phone: `+91 9${randInt(100000000, 999999999)}`,
    email: `${first.toLowerCase()}.${last.toLowerCase()}@greenvalley.edu.in`,
    address: { line1: `${randInt(1, 200)} MG Road`, city: 'Dehradun', state: 'Uttarakhand', pincode: '248001', country: 'India' },
    joiningDate: dateOffset(-randInt(200, 3000)),
    status: rnd() > 0.92 ? 'On Leave' : 'Active',
    qualification: pick(['B.Ed, M.A.', 'M.Sc, B.Ed', 'M.A. English', 'Ph.D', 'B.Tech, M.Ed']),
    experienceYears: randInt(1, 22),
  };
});

// ---------- Students ----------
export const STUDENTS: Student[] = Array.from({ length: 2458 }, (_, i) => {
  const gender = rnd() > 0.48 ? 'Female' : 'Male';
  const { first, last } = fullName(gender as 'Male' | 'Female');
  const name = `${first} ${last}`;
  const cls = pick(CLASSES);
  const section = pick(cls.sections);
  const guardianLast = last;
  const feeStatus = pick(['Paid', 'Paid', 'Paid', 'Pending', 'Overdue', 'Partial'] as const);
  return {
    id: `student-${i}`,
    admissionNo: `GVP${pad(2026000 + i)}`,
    rollNo: `${randInt(1, 45)}`,
    firstName: first,
    lastName: last,
    photo: initialsAvatar(name),
    gender: gender as 'Male' | 'Female',
    dob: `20${randInt(8, 20)}-${pad(randInt(1, 12), 2)}-${pad(randInt(1, 28), 2)}`,
    classId: cls.id,
    className: cls.name,
    sectionId: section.id,
    sectionName: section.name,
    status: rnd() > 0.97 ? 'Inactive' : 'Active',
    guardian: {
      name: `${pick(['Rajesh', 'Sunil', 'Anil', 'Vikram', 'Suresh', 'Deepak', 'Meena', 'Sunita', 'Pooja', 'Kavita'])} ${guardianLast}`,
      relation: pick(['Father', 'Mother', 'Guardian']),
      phone: `+91 9${randInt(100000000, 999999999)}`,
      email: `${first.toLowerCase()}.parent@example.com`,
      occupation: pick(['Business', 'Engineer', 'Doctor', 'Government Service', 'Teacher', 'Homemaker']),
    },
    phone: `+91 9${randInt(100000000, 999999999)}`,
    email: `${first.toLowerCase()}.${last.toLowerCase()}${i}@student.greenvalley.edu.in`,
    address: { line1: `${randInt(1, 300)} Rajpur Road`, city: 'Dehradun', state: 'Uttarakhand', pincode: '248001', country: 'India' },
    bloodGroup: pick(['A+', 'B+', 'O+', 'AB+', 'A-', 'B-', 'O-']),
    admissionDate: dateOffset(-randInt(30, 2500)),
    attendancePercent: randInt(72, 100),
    feeStatus,
    documents: [
      { name: 'Birth Certificate.pdf', type: 'PDF', uploadedOn: dateOffset(-randInt(30, 900)) },
      { name: 'Aadhar Card.pdf', type: 'PDF', uploadedOn: dateOffset(-randInt(30, 900)) },
      { name: 'Previous Report Card.pdf', type: 'PDF', uploadedOn: dateOffset(-randInt(30, 900)) },
    ],
  };
});

// ---------- Attendance ----------
export function generateAttendanceForDate(date: string, classId?: string, sectionId?: string): AttendanceRecord[] {
  let pool = STUDENTS;
  if (classId) pool = pool.filter(s => s.classId === classId);
  if (sectionId) pool = pool.filter(s => s.sectionId === sectionId);
  return pool.slice(0, 45).map((s, i) => ({
    id: `att-${date}-${s.id}`,
    studentId: s.id,
    studentName: `${s.firstName} ${s.lastName}`,
    classId: s.classId,
    sectionId: s.sectionId,
    date,
    status: pick(['Present', 'Present', 'Present', 'Present', 'Absent', 'Late', 'Leave'] as const),
  }));
}

export const TEACHER_ATTENDANCE_TODAY: TeacherAttendanceRecord[] = TEACHERS.slice(0, 40).map(t => ({
  id: `tatt-${t.id}`,
  teacherId: t.id,
  teacherName: `${t.firstName} ${t.lastName}`,
  date: dateOffset(0),
  status: pick(['Present', 'Present', 'Present', 'Absent', 'Leave'] as const),
  checkIn: rnd() > 0.1 ? `0${randInt(7, 8)}:${pad(randInt(0, 59), 2)} AM` : undefined,
  checkOut: rnd() > 0.1 ? `0${randInt(3, 5)}:${pad(randInt(0, 59), 2)} PM` : undefined,
}));

// ---------- Timetable ----------
const DAYS: TimetablePeriod['day'][] = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const SLOTS = [
  ['08:00', '08:45'], ['08:45', '09:30'], ['09:30', '10:15'], ['10:15', '10:30'],
  ['10:30', '11:15'], ['11:15', '12:00'], ['12:00', '12:45'], ['12:45', '13:30'],
];
export function generateTimetable(classId: string, sectionId: string): ClassTimetable {
  const periods: TimetablePeriod[] = [];
  for (const day of DAYS) {
    for (const [start, end] of SLOTS) {
      const isBreak = start === '10:15';
      periods.push({
        day, startTime: start, endTime: end,
        subject: isBreak ? 'Break' : pick(SUBJECTS_LIST),
        teacher: isBreak ? '' : `${pick(FIRST_NAMES_M.concat(FIRST_NAMES_F))} ${pick(LAST_NAMES)}`,
        room: isBreak ? '' : `Room ${randInt(101, 220)}`,
        isBreak,
      });
    }
  }
  return { classId, sectionId, periods };
}

// ---------- Homework ----------
export const HOMEWORK: Homework[] = Array.from({ length: 60 }, (_, i) => {
  const cls = pick(CLASSES);
  const section = pick(cls.sections);
  const total = section.studentCount;
  const submissions = randInt(0, total);
  const due = randInt(-5, 10);
  let status: Homework['status'] = 'Assigned';
  if (due < 0) status = submissions >= total ? 'Graded' : 'Overdue';
  else if (submissions > total * 0.5) status = 'Submitted';
  else status = 'Pending';
  return {
    id: `hw-${i}`,
    title: `${pick(SUBJECTS_LIST)} — ${pick(['Chapter Review', 'Worksheet', 'Practice Problems', 'Essay Writing', 'Lab Report', 'Project Work'])}`,
    description: 'Complete the assigned exercises and submit via the portal before the due date.',
    subject: pick(SUBJECTS_LIST),
    classId: cls.id,
    sectionId: section.id,
    teacherName: `${pick(TEACHERS).firstName} ${pick(TEACHERS).lastName}`,
    assignedDate: dateOffset(due - 7),
    dueDate: dateOffset(due),
    submissions,
    totalStudents: total,
    status,
    attachments: rnd() > 0.5 ? ['worksheet.pdf'] : [],
  };
});

// ---------- Exams & Results ----------
export const EXAMS: Exam[] = [
  { id: 'exam-1', name: 'Unit Test 1', type: 'Unit Test', classId: 'cls-9', startDate: dateOffset(-60), endDate: dateOffset(-55), status: 'Completed', subjects: SUBJECTS_LIST.slice(0, 5).map(s => ({ subject: s, date: dateOffset(-58), maxMarks: 25 })) },
  { id: 'exam-2', name: 'Mid Term Examination', type: 'Mid Term', classId: 'cls-9', startDate: dateOffset(-20), endDate: dateOffset(-12), status: 'Completed', subjects: SUBJECTS_LIST.slice(0, 6).map(s => ({ subject: s, date: dateOffset(-15), maxMarks: 80 })) },
  { id: 'exam-3', name: 'Final Examination', type: 'Final Examination', classId: 'cls-9', startDate: dateOffset(45), endDate: dateOffset(55), status: 'Upcoming', subjects: SUBJECTS_LIST.slice(0, 6).map(s => ({ subject: s, date: dateOffset(48), maxMarks: 100 })) },
  { id: 'exam-4', name: 'Unit Test 2', type: 'Unit Test', classId: 'cls-10', startDate: dateOffset(10), endDate: dateOffset(14), status: 'Upcoming', subjects: SUBJECTS_LIST.slice(0, 5).map(s => ({ subject: s, date: dateOffset(12), maxMarks: 25 })) },
];

function gradeFor(pct: number): string {
  if (pct >= 90) return 'A+';
  if (pct >= 80) return 'A';
  if (pct >= 70) return 'B+';
  if (pct >= 60) return 'B';
  if (pct >= 50) return 'C';
  if (pct >= 35) return 'D';
  return 'F';
}

export function generateMarks(classId: string, subjects: string[]): MarksEntry[] {
  const pool = STUDENTS.filter(s => s.classId === classId).slice(0, 40);
  return pool.map(s => {
    const marks: Record<string, number> = {};
    let total = 0;
    for (const subj of subjects) {
      const m = randInt(28, 100);
      marks[subj] = m;
      total += m;
    }
    const max = subjects.length * 100;
    const percentage = Math.round((total / max) * 1000) / 10;
    return {
      studentId: s.id,
      studentName: `${s.firstName} ${s.lastName}`,
      rollNo: s.rollNo,
      marks,
      total,
      percentage,
      grade: gradeFor(percentage),
      result: percentage >= 35 ? 'Pass' : 'Fail',
    };
  });
}

// ---------- Admissions ----------
export const ADMISSIONS: Admission[] = Array.from({ length: 48 }, (_, i) => {
  const gender = rnd() > 0.5 ? 'Female' : 'Male';
  const { first, last } = fullName(gender as 'Male' | 'Female');
  const name = `${first} ${last}`;
  return {
    id: `adm-${i}`,
    applicationId: `APP${pad(5000 + i)}`,
    studentName: name,
    photo: initialsAvatar(name),
    classAppliedFor: `Class ${pick(CLASS_NAMES)}`,
    guardianName: `${pick(['Rajesh', 'Sunil', 'Anil', 'Meena', 'Sunita'])} ${last}`,
    phone: `+91 9${randInt(100000000, 999999999)}`,
    email: `${first.toLowerCase()}@example.com`,
    applicationDate: dateOffset(-randInt(1, 60)),
    status: pick(['Pending', 'Under Review', 'Approved', 'Approved', 'Rejected', 'Waiting List'] as const),
    previousSchool: pick(['Sunbeam School', 'St. Joseph Academy', 'DAV Public School', 'Delhi Public School', '—']),
  };
});

// ---------- Fees ----------
export const FEE_STRUCTURE = [
  { head: 'Tuition Fee', amount: 45000, frequency: 'Annually' as const },
  { head: 'Admission Fee', amount: 15000, frequency: 'One-time' as const },
  { head: 'Transport Fee', amount: 12000, frequency: 'Annually' as const },
  { head: 'Library Fee', amount: 2000, frequency: 'Annually' as const },
  { head: 'Sports Fee', amount: 3000, frequency: 'Annually' as const },
  { head: 'Laboratory Fee', amount: 4000, frequency: 'Annually' as const },
];

export const STUDENT_FEES: StudentFee[] = STUDENTS.slice(0, 400).map(s => {
  const total = 66000 + randInt(-5000, 15000);
  const paid = s.feeStatus === 'Paid' ? total : s.feeStatus === 'Partial' ? Math.round(total * 0.5) : s.feeStatus === 'Pending' ? 0 : Math.round(total * 0.3);
  return {
    id: `fee-${s.id}`,
    studentId: s.id,
    studentName: `${s.firstName} ${s.lastName}`,
    classSection: `${s.className} - ${s.sectionName}`,
    totalFee: total,
    paid,
    pending: total - paid,
    dueDate: dateOffset(s.feeStatus === 'Overdue' ? -randInt(5, 40) : randInt(5, 60)),
    status: s.feeStatus,
    lastPaymentDate: paid > 0 ? dateOffset(-randInt(2, 90)) : undefined,
  };
});

export const PAYMENTS: Payment[] = STUDENT_FEES.filter(f => f.paid > 0).slice(0, 150).map((f, i) => ({
  id: `pay-${i}`,
  receiptNo: `RCPT${pad(9000 + i)}`,
  studentId: f.studentId,
  studentName: f.studentName,
  amount: Math.round(f.paid / randInt(1, 3)),
  mode: pick(['Cash', 'Card', 'UPI', 'Bank Transfer', 'Cheque'] as const),
  date: f.lastPaymentDate ?? dateOffset(-10),
  head: pick(['Tuition Fee', 'Transport Fee', 'Admission Fee', 'Full Payment']),
}));

// ---------- Library ----------
const BOOK_TITLES = ['Introduction to Physics', 'The Wonders of Chemistry', 'World History Atlas', 'English Grammar Mastery', 'Advanced Mathematics', 'Indian Freedom Struggle', 'Python Programming Basics', 'The Story of My Life', 'Panchatantra Tales', 'Environmental Science', 'Hindi Sahitya Sangrah', 'A Brief History of Time', 'The Discovery of India', 'Elements of Biology', 'Creative Writing Workshop'];
export const BOOKS: Book[] = BOOK_TITLES.map((title, i) => {
  const qty = randInt(5, 40);
  const available = randInt(0, qty);
  return {
    id: `book-${i}`,
    title,
    author: `${pick(['R.K.', 'A.', 'S.', 'M.', 'P.'])} ${pick(LAST_NAMES)}`,
    category: pick(['Science', 'Fiction', 'History', 'Mathematics', 'Language', 'Biography', 'Reference']),
    publisher: pick(['NCERT', 'Oxford Press', 'Penguin', 'Scholastic', 'S. Chand']),
    isbn: `978-${randInt(1000000000, 1999999999)}`,
    quantity: qty,
    available,
    status: available > 0 ? 'Available' : 'Out of Stock',
    coverColor: pick(AVATAR_COLORS),
  };
});

export const LIBRARY_ISSUES: LibraryIssue[] = Array.from({ length: 35 }, (_, i) => {
  const returned = rnd() > 0.4;
  const student = pick(STUDENTS);
  return {
    id: `issue-${i}`,
    bookTitle: pick(BOOK_TITLES),
    memberName: `${student.firstName} ${student.lastName}`,
    memberType: 'Student',
    issueDate: dateOffset(-randInt(5, 30)),
    dueDate: dateOffset(-randInt(-15, 10)),
    returnDate: returned ? dateOffset(-randInt(0, 5)) : undefined,
    status: returned ? 'Returned' : rnd() > 0.6 ? 'Overdue' : 'Issued',
    fine: returned && rnd() > 0.7 ? randInt(10, 100) : undefined,
  };
});

// ---------- Transport ----------
export const VEHICLES: Vehicle[] = Array.from({ length: 14 }, (_, i) => ({
  id: `veh-${i}`,
  number: `UK07 PA ${randInt(1000, 9999)}`,
  type: pick(['Bus (40 seater)', 'Bus (30 seater)', 'Mini Van']),
  capacity: pick([30, 40, 15]),
  driverName: `${pick(FIRST_NAMES_M)} ${pick(LAST_NAMES)}`,
  routeName: `Route ${i + 1}`,
  status: rnd() > 0.1 ? 'Active' : 'Maintenance',
}));

export const TRANSPORT_ROUTES: TransportRoute[] = VEHICLES.map((v, i) => ({
  id: `route-${i}`,
  name: v.routeName,
  vehicleNo: v.number,
  driverName: v.driverName,
  stops: Array.from({ length: randInt(4, 8) }, () => pick(['Rajpur Road', 'Clock Tower', 'ISBT', 'Ballupur', 'GMS Road', 'Sahastradhara', 'Prem Nagar', 'Nehru Colony', 'Race Course', 'Dalanwala'])),
  studentCount: randInt(20, 55),
}));

// ---------- Hostel ----------
export const HOSTEL_ROOMS: HostelRoom[] = Array.from({ length: 40 }, (_, i) => {
  const capacity = pick([1, 2, 4]);
  const occupied = randInt(0, capacity);
  return {
    id: `room-${i}`,
    building: pick(['Himalaya Block', 'Ganga Block', 'Yamuna Block']),
    roomNo: `${pick(['G', '1', '2'])}${randInt(1, 20)}`,
    capacity,
    occupied,
    type: capacity === 1 ? 'Single' : capacity === 2 ? 'Double' : 'Dormitory',
    students: Array.from({ length: occupied }, () => `${pick(FIRST_NAMES_M.concat(FIRST_NAMES_F))} ${pick(LAST_NAMES)}`),
  };
});

// ---------- Calendar Events ----------
export const SCHOOL_EVENTS: SchoolEvent[] = [
  { id: 'ev-1', title: 'Annual Sports Day', type: 'Sports', date: dateOffset(12), time: '09:00 AM', location: 'Main Ground', description: 'Inter-house athletics and sports competitions for all classes.' },
  { id: 'ev-2', title: 'Parent-Teacher Meeting', type: 'Meeting', date: dateOffset(5), time: '10:00 AM', location: 'Classrooms', description: 'Quarterly PTM to discuss student progress.' },
  { id: 'ev-3', title: 'Mid Term Examinations', type: 'Exam', date: dateOffset(20), endDate: dateOffset(28), description: 'Mid term examinations for classes 6 to 12.' },
  { id: 'ev-4', title: 'Gandhi Jayanti', type: 'Holiday', date: '2026-10-02', description: 'National holiday — school closed.' },
  { id: 'ev-5', title: 'Annual Day Celebration', type: 'Function', date: dateOffset(40), time: '05:00 PM', location: 'Auditorium', description: 'Cultural performances and prize distribution ceremony.' },
  { id: 'ev-6', title: 'Science Exhibition Workshop', type: 'Workshop', date: dateOffset(8), time: '11:00 AM', location: 'Science Block', description: 'Hands-on workshop ahead of the annual science exhibition.' },
  { id: 'ev-7', title: 'Diwali Break', type: 'Holiday', date: '2026-11-08', endDate: '2026-11-13', description: 'School closed for Diwali vacations.' },
  { id: 'ev-8', title: 'Republic Day Celebration', type: 'Function', date: '2027-01-26', description: 'Flag hoisting and cultural programme.' },
];

// ---------- Notices ----------
export const NOTICES: Notice[] = [
  { id: 'not-1', title: 'Mid Term Examination Schedule Released', description: 'The mid term examination datesheet for classes 6–12 has been published. Students can check the exam module for subject-wise dates.', audience: 'Students', publishDate: dateOffset(-3), expiryDate: dateOffset(25), status: 'Published' },
  { id: 'not-2', title: 'Fee Payment Reminder for Quarter 2', description: 'Parents are requested to clear pending Quarter 2 fees before the due date to avoid late fee charges.', audience: 'Parents', publishDate: dateOffset(-5), expiryDate: dateOffset(15), status: 'Published' },
  { id: 'not-3', title: 'Annual Sports Day — Practice Schedule', description: 'All participating students must report for practice sessions as per the schedule shared by respective sports teachers.', audience: 'Specific Class', publishDate: dateOffset(-2), expiryDate: dateOffset(12), status: 'Published' },
  { id: 'not-4', title: 'Staff Meeting — Curriculum Planning', description: 'All teaching staff are requested to attend the curriculum planning meeting in the staff room.', audience: 'Teachers', publishDate: dateOffset(-1), expiryDate: dateOffset(6), status: 'Published' },
  { id: 'not-5', title: 'Holiday Notice — Diwali Break', description: 'The school will remain closed from Nov 8 to Nov 13 on account of Diwali. Classes resume Nov 14.', audience: 'All', publishDate: dateOffset(-1), expiryDate: dateOffset(60), status: 'Published' },
  { id: 'not-6', title: 'New Library Books Added', description: 'A fresh set of reference books and fiction titles have been added to the library catalogue this month.', audience: 'All', publishDate: dateOffset(-10), expiryDate: dateOffset(-1), status: 'Expired' },
];

// ---------- Notifications ----------
export const NOTIFICATIONS: AppNotification[] = [
  { id: 'ntf-1', title: 'Attendance Alert', message: 'Ananya Verma was marked absent today.', type: 'Attendance', date: dateOffset(0), read: false },
  { id: 'ntf-2', title: 'Fee Payment Received', message: 'Payment of ₹45,000 received from Rohan Kumar.', type: 'Fees', date: dateOffset(0), read: false },
  { id: 'ntf-3', title: 'New Homework Assigned', message: 'Mathematics homework assigned for Class 9-A.', type: 'Homework', date: dateOffset(-1), read: true },
  { id: 'ntf-4', title: 'Exam Schedule Updated', message: 'Mid term exam schedule for Class 10 has been revised.', type: 'Examination', date: dateOffset(-1), read: true },
  { id: 'ntf-5', title: 'Emergency: Early Dismissal', message: 'School will close 2 hours early today due to heavy rainfall.', type: 'Emergency', date: dateOffset(-2), read: true },
  { id: 'ntf-6', title: 'General Notice', message: 'PTA meeting minutes have been published on the portal.', type: 'General', date: dateOffset(-3), read: true },
];

// ---------- Recent Activities ----------
export const ACTIVITIES: Activity[] = [
  { id: 'act-1', actor: 'Priya Sharma', action: 'added a new student — Kabir Singh', timestamp: '10 minutes ago', icon: 'user-plus' },
  { id: 'act-2', actor: 'Accounts Office', action: 'received a fee payment of ₹32,000', timestamp: '32 minutes ago', icon: 'wallet' },
  { id: 'act-3', actor: 'Rohan Verma', action: 'published a new assignment for Class 8-B', timestamp: '1 hour ago', icon: 'book' },
  { id: 'act-4', actor: 'System', action: 'marked teacher attendance for the day', timestamp: '2 hours ago', icon: 'check-circle' },
  { id: 'act-5', actor: 'Exam Cell', action: 'updated the Mid Term exam schedule', timestamp: '3 hours ago', icon: 'calendar' },
  { id: 'act-6', actor: 'Anjali Mehta', action: 'uploaded results for Class 10 Unit Test', timestamp: '5 hours ago', icon: 'award' },
];

export function currentSessionLabel(): string {
  return SESSIONS.find(s => s.status === 'Active')?.label ?? SESSIONS[0].label;
}
