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
    ],
  },
];
