import { IconName } from '../../../shared/components/icon/icons';

export interface NavLink {
  label: string;
  /** In-page anchor on the landing page ("/#features") or a page route. */
  route: string;
  fragment?: string;
}

/** Header navigation (also reused by the footer). There is deliberately no Pricing entry. */
export const PUBLIC_NAV: NavLink[] = [
  { label: 'Features', route: '/', fragment: 'features' },
  { label: 'Modules', route: '/', fragment: 'modules' },
  { label: 'How it works', route: '/', fragment: 'how-it-works' },
  { label: 'Contact', route: '/contact' },
];

export interface Feature {
  icon: IconName;
  title: string;
  text: string;
}

export const FEATURES: Feature[] = [
  { icon: 'building-2', title: 'Multi-school ready', text: 'Every school gets its own private space, branding and data. One platform, many schools, nothing mixed.' },
  { icon: 'users', title: 'Role-based access', text: 'School Admin, Principal, Teacher, Accountant, Admission Staff, Student and Guardian each see only what they need.' },
  { icon: 'shield', title: 'Secure by design', text: 'Strict tenant isolation, protected records and private student photos and documents.' },
  { icon: 'smartphone', title: 'Mobile friendly', text: 'Works on the phone in the staffroom or at the gate, with the same screens as on a computer.' },
  { icon: 'graduation-cap', title: 'Student and parent portal', text: 'Students follow homework, results, fees and notices from their own login.' },
  { icon: 'bar-chart', title: 'Reports that help', text: 'Attendance, fee collection and examination reports, ready when the Principal asks.' },
];

export interface ModuleInfo {
  icon: IconName;
  title: string;
  text: string;
  comingSoon?: boolean;
}

export const MODULES: ModuleInfo[] = [
  { icon: 'inbox', title: 'Admissions', text: 'Take applications, track documents and approve or reject in one pipeline.' },
  { icon: 'user', title: 'Student and Guardian Management', text: 'Complete student records, guardians, ID cards and bulk import.' },
  { icon: 'circle-check', title: 'Attendance', text: 'Class teachers mark daily attendance; monthly reports for the school.' },
  { icon: 'file-text', title: 'Examination and Report Cards', text: 'Grade scales, report schemes, marks entry, results and printable report cards.' },
  { icon: 'wallet', title: 'Fees and Finance', text: 'Fee structures, concessions, receipts and a live collection dashboard.' },
  { icon: 'calendar', title: 'Timetable', text: 'Bell schedule and a weekly grid for every section; teachers see their own.' },
  { icon: 'megaphone', title: 'Notices and Communication', text: 'Publish notices to everyone, staff, or students and guardians, with read tracking.' },
  { icon: 'book-open', title: 'Library', text: 'Catalogue, issue and return of books.', comingSoon: true },
];

export interface Step {
  title: string;
  text: string;
}

export const STEPS: Step[] = [
  { title: 'Register', text: 'We register your school and send the School Admin login. No installation, nothing to host.' },
  { title: 'Set up', text: 'Add the academic year, classes, sections, subjects and staff, or import them from a spreadsheet.' },
  { title: 'Go live', text: 'Admit students, mark attendance, collect fees and publish results from day one.' },
];
