export interface DashboardStat {
  label: string;
  value: string;
  change: number; // percentage, positive or negative
  changeLabel: string;
  icon: string;
}

export interface ActivityItem {
  id: number;
  actor: string;
  action: string;
  target: string;
  time: string;
  icon: string;
}

export interface QuickAction {
  label: string;
  icon: string;
  description: string;
}

export interface RecentUserItem {
  id: string;
  name: string;
  role: string;
  status: 'Active' | 'Inactive' | 'Pending';
}

export interface SystemStatusItem {
  label: string;
  status: 'operational' | 'degraded' | 'down';
  detail: string;
}

export const DASHBOARD_STATS: DashboardStat[] = [
  { label: 'Total Organizations', value: '48', change: 8.2, changeLabel: 'Compared to last month', icon: 'building-2' },
  { label: 'Active Users', value: '12,480', change: 12.5, changeLabel: 'Compared to last month', icon: 'users' },
  { label: 'Total Students', value: '9,214', change: 4.1, changeLabel: 'Compared to last month', icon: 'graduation-cap' },
  { label: 'Revenue', value: '$84,320', change: -2.4, changeLabel: 'Compared to last month', icon: 'banknote' },
];

export const RECENT_ACTIVITY: ActivityItem[] = [
  { id: 1, actor: 'Priya Sharma', action: 'created a new user', target: 'Akshay Kumar', time: '5 minutes ago', icon: 'user-plus' },
  { id: 2, actor: 'System', action: 'generated invoice for', target: 'ABC International School', time: '32 minutes ago', icon: 'file-text' },
  { id: 3, actor: 'Admin User', action: 'updated theme for', target: 'Demo School', time: '1 hour ago', icon: 'palette' },
  { id: 4, actor: 'Meera Nair', action: 'submitted a support ticket about', target: 'Billing', time: '3 hours ago', icon: 'life-buoy' },
  { id: 5, actor: 'System', action: 'completed nightly backup for', target: 'all organizations', time: 'Yesterday', icon: 'database-backup' },
];

export const QUICK_ACTIONS: QuickAction[] = [
  { label: 'Add User', icon: 'user-plus', description: 'Invite a new team member' },
  { label: 'Create Organization', icon: 'building-2', description: 'Onboard a new tenant' },
  { label: 'View Reports', icon: 'bar-chart-3', description: 'Open analytics & reports' },
  { label: 'Settings', icon: 'settings', description: 'Configure application settings' },
];

export const RECENT_USERS: RecentUserItem[] = [
  { id: 'u1', name: 'Priya Sharma', role: 'Manager', status: 'Active' },
  { id: 'u2', name: 'Akshay Kumar', role: 'Staff', status: 'Pending' },
  { id: 'u3', name: 'Meera Nair', role: 'Viewer', status: 'Inactive' },
  { id: 'u4', name: 'Rohit Verma', role: 'Staff', status: 'Active' },
];

export const SYSTEM_STATUS: SystemStatusItem[] = [
  { label: 'API', status: 'operational', detail: 'All systems normal' },
  { label: 'Database', status: 'operational', detail: 'All systems normal' },
  { label: 'Background Jobs', status: 'degraded', detail: 'Slight delay in queue processing' },
  { label: 'Email Delivery', status: 'operational', detail: 'All systems normal' },
];

/** Simple monthly revenue series for the dashboard mini chart (no chart lib needed). */
export const REVENUE_SERIES: { month: string; value: number }[] = [
  { month: 'Apr', value: 42 },
  { month: 'May', value: 55 },
  { month: 'Jun', value: 48 },
  { month: 'Jul', value: 63 },
  { month: 'Aug', value: 59 },
  { month: 'Sep', value: 71 },
  { month: 'Oct', value: 84 },
];
