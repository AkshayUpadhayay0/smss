import { IconName } from '../../shared/components/icon/icons';

export interface NavItem {
  label: string;
  icon: IconName;
  /** Leaf items have a route; groups have children. */
  route?: string;
  children?: { label: string; route: string }[];
}

export const NAV_ITEMS: NavItem[] = [
  { label: 'Dashboard', icon: 'layout-dashboard', route: '/dashboard' },
  { label: 'School Profile', icon: 'home', route: '/school-profile' },
  { label: 'Students', icon: 'graduation-cap', route: '/students' },
  { label: 'Admissions', icon: 'user', route: '/admissions' },
  { label: 'Add Existing Student', icon: 'inbox', route: '/students/onboard-existing' },
  {
    label: 'School Setup',
    icon: 'graduation-cap',
    children: [
      { label: 'Import Masters', route: '/school-setup/import-masters' },
      { label: 'Academic Year', route: '/school-setup/academic-year' },
      { label: 'Class', route: '/school-setup/classes' },
      { label: 'Section', route: '/school-setup/sections' },
      { label: 'Subject', route: '/school-setup/subjects' },
      { label: 'Class-Subject Mapping', route: '/school-setup/class-subjects' },
      { label: 'Employee Designation', route: '/school-setup/employee-designations' },
      { label: 'Employee Department', route: '/school-setup/employee-departments' },
      { label: 'Admission Type', route: '/school-setup/admission-types' },
      { label: 'Document Types', route: '/school-setup/document-types' },
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
