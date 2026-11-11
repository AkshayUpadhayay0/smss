import { EmptyState } from '../../../src/components/EmptyState';
import { HomeHeader } from '../../../src/components/HomeHeader';
import { Screen } from '../../../src/components/Screen';

// Phase 1/2 shell: the avatar opens the shared profile sheet (sign-out lives there). The real Teacher Home is built in Phase 3.
export default function TeacherHomeTab() {
  return (
    <Screen>
      <HomeHeader subtitle="Teacher" />
      <EmptyState icon="clock" title="Coming soon" message="This screen is not built yet." />
    </Screen>
  );
}
