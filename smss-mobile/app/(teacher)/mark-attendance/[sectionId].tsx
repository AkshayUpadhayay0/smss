import { useLocalSearchParams } from 'expo-router';
import { MarkAttendanceScreen } from '../../../src/features/teacher/MarkAttendanceScreen';

// A detail screen above the tabs: no tab bar.
export default function MarkAttendance() {
  const { sectionId } = useLocalSearchParams<{ sectionId: string }>();
  return <MarkAttendanceScreen sectionId={Number(sectionId)} />;
}
