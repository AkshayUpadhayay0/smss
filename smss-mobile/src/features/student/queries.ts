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
  bus: ['student', 'my-bus'] as const,
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

/**
 * The student's bus. Polled every `pollSeconds` ONLY while the screen is focused (and, through react-query's focus manager, only while the app is in the foreground);
 * the answer is never reused for long: a position older than a few seconds is stale news.
 */
export const useMyBus = (pollSeconds: number | false) =>
  useQuery({ queryKey: studentKeys.bus, queryFn: student.getMyBus, refetchInterval: pollSeconds === false ? false : pollSeconds * 1000, staleTime: 0, gcTime: 60_000 });
