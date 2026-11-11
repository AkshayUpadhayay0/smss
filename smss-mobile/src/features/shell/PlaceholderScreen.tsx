import { EmptyState } from '../../components/EmptyState';
import { Screen } from '../../components/Screen';

/** Phase 1 tab shell: no data, no invented content. Replaced screen by screen in Phases 2 and 3. */
export function PlaceholderScreen({ title }: { title: string }) {
  return (
    <Screen title={title}>
      <EmptyState icon="clock" title="Coming soon" message="This screen is not built yet." />
    </Screen>
  );
}
