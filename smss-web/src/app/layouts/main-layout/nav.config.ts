import { canAccess } from '../../core/config/route-roles';
import { IconName } from '../../shared/components/icon/icons';

export interface NavItem {
  label: string;
  icon: IconName;
  /** Leaf items have a route; groups have children. */
  route?: string;
  children?: { label: string; route: string }[];
  /** A group that shows a single child for the signed-in role renders that child as a plain item instead. */
  flatWhenSingle?: boolean;
  /** Shown only to logins that have an employee record (a School Admin login is the school itself and has none). */
  needsEmployeeRecord?: boolean;
  /** Shows a count next to the label. The sidebar knows where each named count comes from. */
  badge?: 'newEnquiries';
}

export const NAV_ITEMS: NavItem[] = [
  { label: 'Dashboard', icon: 'layout-dashboard', route: '/dashboard' },
  { label: 'Calendar', icon: 'calendar', route: '/calendar' },
  { label: 'Timetable', icon: 'calendar', route: '/timetable' },
  { label: 'My Timetable', icon: 'calendar', route: '/timetable/my' },
  { label: 'Mark Attendance', icon: 'circle-check', route: '/attendance/mark' },
  { label: 'Attendance Report', icon: 'file-text', route: '/attendance/report' },
  { label: 'Notices', icon: 'bell', route: '/notices' },
  { label: 'My Notices', icon: 'bell', route: '/notices/my' },
  { label: 'Homework', icon: 'file-text', route: '/homework' },
  { label: 'My Homework', icon: 'file-text', route: '/homework/my' },
  { label: 'Assessments', icon: 'file-text', route: '/assessments' },
  { label: 'My Results', icon: 'file-text', route: '/assessments/my' },
  { label: 'My Leave', icon: 'file-text', route: '/leave/my', needsEmployeeRecord: true },
  { label: 'My Leave', icon: 'file-text', route: '/leave/mine' },
  { label: 'Leave Requests', icon: 'inbox', route: '/leave/requests' },
  { label: 'My Profile', icon: 'user', route: '/my-student-profile' },
  { label: 'My Fees', icon: 'file-text', route: '/my-fees' },
  { label: 'My Report Cards', icon: 'file-text', route: '/my-report-cards' },
  { label: 'School Profile', icon: 'home', route: '/school-profile' },
  {
    label: 'Students',
    icon: 'graduation-cap',
    flatWhenSingle: true,
    children: [
      { label: 'Students', route: '/students' },
      { label: 'My Students', route: '/my-students' },
      { label: 'Admissions', route: '/admissions' },
      { label: 'Add Existing Student', route: '/students/onboard-existing' },
      { label: 'Import Students', route: '/students/import' },
      { label: 'Student Leave', route: '/leave/students' },
      { label: 'Fee Status', route: '/fee-status' },
    ],
  },
  {
    // Examination / Report Card. A role that sees one child (a Teacher: the exam schedule) gets it as a plain item.
    label: 'Examinations',
    icon: 'file-text',
    flatWhenSingle: true,
    children: [
      { label: 'Grade Scales', route: '/examinations/grade-scales' },
      { label: 'Report Schemes', route: '/examinations/schemes' },
      { label: 'Report Card Settings', route: '/examinations/report-card-settings' },
      { label: 'Activity Areas', route: '/examinations/activity-areas' },
      { label: 'Exam Schedule', route: '/examinations/schedule' },
      { label: 'Marks Entry', route: '/examinations/marks' },
      { label: 'My Class Report', route: '/examinations/class-report' },
      { label: 'Results and Publish', route: '/examinations/results' },
    ],
  },
  {
    label: 'School Setup',
    icon: 'graduation-cap',
    children: [
      { label: 'Import Masters', route: '/school-setup/import-masters' },
      { label: 'Academic Year', route: '/school-setup/academic-year' },
      { label: 'Class', route: '/school-setup/classes' },
      { label: 'Section', route: '/school-setup/sections' },
      { label: 'Periods', route: '/school-setup/periods' },
      { label: 'Subject', route: '/school-setup/subjects' },
      { label: 'Class-Subject Mapping', route: '/school-setup/class-subjects' },
      { label: 'Employee Designation', route: '/school-setup/employee-designations' },
      { label: 'Employee Department', route: '/school-setup/employee-departments' },
      { label: 'Admission Type', route: '/school-setup/admission-types' },
      { label: 'Document Types', route: '/school-setup/document-types' },
      { label: 'ID Card Settings', route: '/school-setup/id-card' },
    ],
  },
  {
    label: 'Employees',
    icon: 'user',
    children: [
      { label: 'All Employees', route: '/employees' },
      { label: 'Add Employee', route: '/employees/new' },
    ],
  },
  {
    label: 'Fee Setup',
    icon: 'file-text',
    children: [
      { label: 'Fee Types', route: '/fee-setup/fee-types' },
      { label: 'Fee Structures', route: '/fee-setup/fee-structures' },
      { label: 'Fee Concessions', route: '/fee-setup/fee-concessions' },
    ],
  },
  {
    label: 'Fees',
    icon: 'layout-dashboard',
    children: [
      { label: 'Fee Dashboard', route: '/fee-dashboard' },
      { label: 'Class-wise Report', route: '/fee-reports/class' },
    ],
  },
  { label: 'Registered Schools', icon: 'building-2', route: '/schools' },
  { label: 'Enquiries', icon: 'inbox', route: '/enquiries', badge: 'newEnquiries' },
  { label: 'Subscriptions', icon: 'star', route: '/subscriptions' },
  { label: 'Invoices', icon: 'file-text', route: '/invoices' },
  {
    label: 'Company Finance',
    icon: 'building-2',
    children: [
      { label: 'Overview', route: '/company/overview' },
      { label: 'Employees', route: '/company/employees' },
      { label: 'Expenses', route: '/company/expenses' },
    ],
  },
  {
    label: 'Masters',
    icon: 'layers',
    children: [
      { label: 'Board Type', route: '/masters/board-type' },
      { label: 'School Type', route: '/masters/school-type' },
      { label: 'School Level', route: '/masters/school-level' },
      { label: 'Status', route: '/masters/status' },
      { label: 'Role', route: '/masters/role' },
      { label: 'Religion/Caste Category', route: '/masters/religion-category' },
      { label: 'Blood Group', route: '/masters/blood-group' },
      { label: 'Gender', route: '/masters/gender' },
      { label: 'Student Category', route: '/masters/student-category' },
    ],
  },
];

/**
 * What the sidebar shows for `roles`: leaves the role may open, groups cut down to the children it may open (a group
 * with none disappears; a `flatWhenSingle` group with one becomes that child as a plain item). Presentation only: the
 * route guard still decides who may actually enter a route, from the same ROUTE_ROLES.
 */
export function visibleNav(roles: readonly string[], hasEmployeeRecord = true): NavItem[] {
  return NAV_ITEMS.flatMap((item): NavItem[] => {
    if (item.children) {
      const children = item.children.filter((c) => canAccess(c.route, roles));
      if (!children.length) return [];
      if (children.length === 1 && item.flatWhenSingle) return [{ label: children[0].label, icon: item.icon, route: children[0].route }];
      return [{ ...item, children }];
    }
    if (item.needsEmployeeRecord && !hasEmployeeRecord) return [];
    return item.route && canAccess(item.route, roles) ? [item] : [];
  });
}
