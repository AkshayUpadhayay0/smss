import { useQuery } from '@tanstack/react-query';
import * as student from '../../api/student';

// One place for the student app's query keys and hooks. The cache is cleared on sign-in / sign-out (src/auth/session.tsx), so keys need no user id.
export const studentKeys = {
  dashboard: ['student', 'dashboard'] as const,
  homework: ['student', 'homework'] as const,
  marks: ['student', 'marks'] as const,
  timetable: ['student', 'timetable'] as const,
  notices: ['student', 'notices'] as const,
  unread: ['student', 'unread-count'] as const,
  attendance: (from: string, to: string) => ['student', 'attendance', from, to] as const,
};

export const useDashboard = () => useQuery({ queryKey: studentKeys.dashboard, queryFn: student.getDashboard });
export const useHomework = () => useQuery({ queryKey: studentKeys.homework, queryFn: student.getHomework });
export const useMarks = () => useQuery({ queryKey: studentKeys.marks, queryFn: student.getMarks });
export const useTimetable = () => useQuery({ queryKey: studentKeys.timetable, queryFn: student.getTimetable });
export const useNotices = () => useQuery({ queryKey: studentKeys.notices, queryFn: student.getNotices });
export const useUnreadCount = () => useQuery({ queryKey: studentKeys.unread, queryFn: student.getUnreadCount });
export const useOwnAttendance = (from: string, to: string, enabled = true) =>
  useQuery({ queryKey: studentKeys.attendance(from, to), queryFn: () => student.getOwnAttendance(from, to), enabled });

/** The server's "today" (from the dashboard). Undefined until the dashboard has loaded. */
export function useServerToday() {
  const dashboard = useDashboard();
  return { today: dashboard.data?.today, query: dashboard };
}
