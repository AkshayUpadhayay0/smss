import { useQuery } from '@tanstack/react-query';
import * as family from '../../api/family';

// One place for the parent app's query keys and hooks. The cache is cleared on sign-in / sign-out (src/auth/session.tsx), so keys need no user id.
export const familyKeys = {
  children: ['family', 'children'] as const,
  notices: ['family', 'notices'] as const,
  unread: ['family', 'unread'] as const,
  calendar: (from: string, to: string) => ['family', 'calendar', from, to] as const,
  child: (id: string, part: string, ...extra: string[]) => ['family', 'child', id, part, ...extra] as const,
};

export const useChildren = () => useQuery({ queryKey: familyKeys.children, queryFn: family.getChildren });
export const useNotices = () => useQuery({ queryKey: familyKeys.notices, queryFn: family.getNotices });
export const useUnread = () => useQuery({ queryKey: familyKeys.unread, queryFn: family.getUnreadNotices });
export const useCalendar = (from: string, to: string) => useQuery({ queryKey: familyKeys.calendar(from, to), queryFn: () => family.getCalendar(from, to) });

export const useProfile = (id: string) => useQuery({ queryKey: familyKeys.child(id, 'profile'), queryFn: () => family.getProfile(id) });
export const useAttendance = (id: string, from: string, to: string) => useQuery({ queryKey: familyKeys.child(id, 'attendance', from, to), queryFn: () => family.getAttendance(id, from, to) });
export const useHomework = (id: string) => useQuery({ queryKey: familyKeys.child(id, 'homework'), queryFn: () => family.getHomework(id) });
export const useTimetable = (id: string) => useQuery({ queryKey: familyKeys.child(id, 'timetable'), queryFn: () => family.getTimetable(id) });
export const useExams = (id: string) => useQuery({ queryKey: familyKeys.child(id, 'exams'), queryFn: () => family.getExamSchedule(id) });
export const useReportCards = (id: string) => useQuery({ queryKey: familyKeys.child(id, 'reports'), queryFn: () => family.getReportCards(id) });
export const useFees = (id: string) => useQuery({ queryKey: familyKeys.child(id, 'fees'), queryFn: () => family.getFees(id) });
export const useLeave = (id: string) => useQuery({ queryKey: familyKeys.child(id, 'leave'), queryFn: () => family.getLeave(id) });
export const useReceipt = (id: string, paymentId: number) => useQuery({ queryKey: familyKeys.child(id, 'receipt', String(paymentId)), queryFn: () => family.getReceipt(id, paymentId) });
