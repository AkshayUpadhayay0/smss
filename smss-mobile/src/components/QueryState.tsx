import type { ReactNode } from 'react';
import { View } from 'react-native';
import { messageOf } from '../api/errors';
import { EmptyState } from './EmptyState';
import { ErrorState } from './ErrorState';
import type { IconName } from './Icon';
import { SkeletonCard } from './Skeleton';

export interface QueryStateProps {
  loading: boolean;
  error: unknown;
  onRetry: () => void;
  /** When set, shown instead of the children (the list is empty). */
  empty?: { title: string; message?: string; icon?: IconName } | null;
  skeletons?: number;
  children: ReactNode;
}

/** The standard list-screen states: skeleton while loading, error with Retry, empty state, then the content. */
export function QueryState({ loading, error, onRetry, empty, skeletons = 3, children }: QueryStateProps) {
  if (loading) {
    return (
      <View style={{ gap: 12 }} accessibilityLabel="Loading" accessibilityLiveRegion="polite">
        {Array.from({ length: skeletons }, (_, i) => (
          <SkeletonCard key={i} />
        ))}
      </View>
    );
  }
  if (error) return <ErrorState message={messageOf(error)} onRetry={onRetry} />;
  if (empty) return <EmptyState title={empty.title} message={empty.message} icon={empty.icon} />;
  return <>{children}</>;
}
