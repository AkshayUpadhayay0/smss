import { useQuery } from '@tanstack/react-query';
import * as teacher from '../../api/teacher';

// The teacher app's query keys and hooks. The cache is cleared on sign-in / sign-out, so keys need no user id.
export const teacherKeys = {
  dashboard: ['teacher', 'dashboard'] as const,
  timetable: ['teacher', 'timetable'] as const,
  mySections: ['teacher', 'my-sections'] as const,
  sectionAttendance: (id: number) => ['teacher', 'section-attendance', id] as const,
  homework: ['teacher', 'homework'] as const,
  types: ['teacher', 'assessment-types'] as const,
  roster: (year: number, section: number, subject: number) => ['teacher', 'roster', year, section, subject] as const,
  leave: ['teacher', 'student-leave'] as const,
};

export const useTeacherDashboard = () => useQuery({ queryKey: teacherKeys.dashboard, queryFn: teacher.getTeacherDashboard });
export const useMyTimetable = () => useQuery({ queryKey: teacherKeys.timetable, queryFn: teacher.getMyTimetable });
export const useMySections = (enabled = true) => useQuery({ queryKey: teacherKeys.mySections, queryFn: teacher.getMySections, enabled });
export const useMyHomework = () => useQuery({ queryKey: teacherKeys.homework, queryFn: teacher.getMyHomework });
export const useAssessmentTypes = () => useQuery({ queryKey: teacherKeys.types, queryFn: teacher.getAssessmentTypes, staleTime: 5 * 60_000 });
export const useStudentLeave = (enabled = true) => useQuery({ queryKey: teacherKeys.leave, queryFn: teacher.getStudentLeave, enabled });

/**
 * The roster being marked. A form is seeded from this, so it must not silently refetch under the teacher's unsaved marks: no refetch on focus /
 * reconnect, and a long staleTime. An explicit pull-to-refresh or Retry still reloads it.
 */
export const useSectionAttendance = (sectionId: number) =>
  useQuery({
    queryKey: teacherKeys.sectionAttendance(sectionId),
    queryFn: () => teacher.getSectionAttendance(sectionId),
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
    staleTime: Infinity,
    gcTime: 0,
  });

/** The students a test must cover (same no-silent-refetch reasoning as above). */
export const useAssessmentRoster = (year: number | null, section: number | null, subject: number | null) =>
  useQuery({
    queryKey: teacherKeys.roster(year ?? 0, section ?? 0, subject ?? 0),
    queryFn: () => teacher.getAssessmentRoster(year as number, section as number, subject as number),
    enabled: year !== null && section !== null && subject !== null,
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
    staleTime: Infinity,
    gcTime: 0,
  });
