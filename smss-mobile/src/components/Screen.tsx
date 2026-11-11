import type { ReactNode } from 'react';
import { RefreshControl, ScrollView, StyleSheet, View, type StyleProp, type ViewStyle } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { colors, spacing } from '../theme/tokens';
import { Text } from './Text';

export interface ScreenProps {
  title?: string;
  subtitle?: string;
  children: ReactNode;
  /** Pull-to-refresh. Every list screen passes this. */
  onRefresh?: () => void;
  refreshing?: boolean;
  /** Edges to pad for. Screens inside the tab bar leave the bottom to the tab bar. */
  edges?: ('top' | 'bottom' | 'left' | 'right')[];
  contentStyle?: StyleProp<ViewStyle>;
}

/** Safe-area-aware scrolling page with the standard gutter and an optional title. */
export function Screen({ title, subtitle, children, onRefresh, refreshing = false, edges = ['top', 'left', 'right'], contentStyle }: ScreenProps) {
  return (
    <SafeAreaView style={styles.safe} edges={edges}>
      <ScrollView
        contentContainerStyle={[styles.content, contentStyle]}
        keyboardShouldPersistTaps="handled"
        refreshControl={onRefresh ? <RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={colors.primary} colors={[colors.primary]} /> : undefined}
      >
        {title ? (
          <View style={styles.header}>
            <Text variant="title" accessibilityRole="header">
              {title}
            </Text>
            {subtitle ? <Text variant="small">{subtitle}</Text> : null}
          </View>
        ) : null}
        {children}
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  content: { padding: spacing.lg, gap: spacing.md, flexGrow: 1 },
  header: { gap: 2, paddingBottom: spacing.sm },
});
