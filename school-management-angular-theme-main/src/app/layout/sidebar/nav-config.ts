export interface NavItem {
  label: string;
  icon: string;
  link?: string;
  children?: NavItem[];
  badge?: string;
}

export interface NavSection {
  title: string;
  items: NavItem[];
}

export const NAV_SECTIONS: NavSection[] = [
  {
    title: 'Main',
    items: [
      { label: 'Dashboard', icon: 'dashboard', link: '/dashboard' },
    ],
  },
  {
    title: 'Academic',
    items: [
      { label: 'Students', icon: 'users', link: '/students' },
      { label: 'Teachers / Staff', icon: 'user-check', link: '/teachers' },
      { label: 'Classes', icon: 'layers', link: '/classes' },
      { label: 'Subjects', icon: 'book', link: '/subjects' },
      { label: 'Academic Sessions', icon: 'calendar', link: '/sessions' },
      { label: 'Timetable', icon: 'clock', link: '/timetable' },
      { label: 'Homework', icon: 'clipboard-list', link: '/homework' },
      {
        label: 'Examinations', icon: 'award',
        children: [
          { label: 'Exam Schedule', icon: 'calendar', link: '/examinations' },
          { label: 'Marks Entry', icon: 'edit', link: '/examinations/marks' },
          { label: 'Results', icon: 'check-square', link: '/results' },
        ],
      },
      { label: 'Attendance', icon: 'check-square', link: '/attendance' },
    ],
  },
  {
    title: 'Communication',
    items: [
      { label: 'Notices', icon: 'megaphone', link: '/notices' },
      { label: 'Notifications', icon: 'bell', link: '/notifications' },
      { label: 'Messages', icon: 'message-square', link: '/messages' },
      { label: 'Events / Calendar', icon: 'calendar', link: '/calendar' },
    ],
  },
  {
    title: 'Administration',
    items: [
      { label: 'Admissions', icon: 'user-plus', link: '/admissions' },
      { label: 'Fees / Payments', icon: 'wallet', link: '/fees' },
      { label: 'Transport', icon: 'bus', link: '/transport' },
      { label: 'Hostel', icon: 'bed', link: '/hostel' },
      { label: 'Library', icon: 'book-open', link: '/library' },
    ],
  },
  {
    title: 'Reports',
    items: [
      { label: 'Reports', icon: 'bar-chart', link: '/reports' },
    ],
  },
  {
    title: 'System',
    items: [
      { label: 'UI Components', icon: 'grid', link: '/ui' },
      { label: 'Settings', icon: 'settings', link: '/settings' },
      { label: 'Profile', icon: 'user', link: '/profile' },
    ],
  },
];
