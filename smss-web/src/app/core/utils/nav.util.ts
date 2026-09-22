import { NavItem } from '../models';

export const NAV_ITEMS: NavItem[] = [
  { label: 'Dashboard', route: '/dashboard', icon: 'layout-dashboard' },
  { label: 'Registered Schools', route: '/schools', icon: 'home' },
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
