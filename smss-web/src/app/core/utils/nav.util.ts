import { NavItem } from '../models';

export const NAV_ITEMS: NavItem[] = [
  { label: 'Dashboard', route: '/dashboard', icon: 'layout-dashboard' },
  { label: 'Registered Schools', route: '/schools', icon: 'home' },
  {
    label: 'Masters',
    icon: 'book-open',
    children: [
      { label: 'Board Type', route: '/master-data/board-types', icon: 'table-2' },
      { label: 'School Type', route: '/master-data/school-types', icon: 'table-2' },
      { label: 'School Level', route: '/master-data/school-levels', icon: 'table-2' },
      { label: 'Status', route: '/master-data/status', icon: 'table-2' },
      { label: 'Role', route: '/master-data/roles', icon: 'table-2' },
    ],
  },
  {
    label: 'Management',
    icon: 'layout-grid',
    children: [
      { label: 'Table', route: '/table', icon: 'table-2' },
      { label: 'Form', route: '/form', icon: 'file-text' },
      { label: 'UI Elements', route: '/ui-elements', icon: 'layout-grid' },
      { label: 'Modals', route: '/modals', icon: 'square-check-big' },
    ],
  },
  {
    label: 'Documentation',
    icon: 'book-open',
    children: [{ label: 'Swagger', route: '/swagger', icon: 'code' }],
  },
  {
    label: 'Examples',
    icon: 'layers',
    children: [{ label: 'Nested Breadcrumbs', route: '/example/a', icon: 'rows-3' }],
  },
  {
    label: 'Authentication',
    icon: 'shield-check',
    children: [
      { label: 'Login', route: '/login', icon: 'log-in' },
      { label: 'Registration', route: '/register', icon: 'user-plus' },
    ],
  },
];
