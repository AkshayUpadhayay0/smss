import { useLocalSearchParams } from 'expo-router';
import { ChildDashboard } from '../../../src/features/family/ChildScreen';

export default function ChildRoute() {
  const { studentId, tab } = useLocalSearchParams<{ studentId: string; tab?: string }>();
  return <ChildDashboard studentId={String(studentId)} initialTab={typeof tab === 'string' ? tab : undefined} />;
}
