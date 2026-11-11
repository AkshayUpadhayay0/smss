import type { ReactNode } from 'react';
import { Pressable, RefreshControl, ScrollView, StyleSheet, View, type StyleProp, type ViewStyle } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { colors, MIN_TOUCH, spacing } from '../theme/tokens';
import { Icon } from './Icon';
import { Text } from './Text';

export interface ScreenProps {
  title?: string;
  subtitle?: string;
  children: ReactNode;
  /** Detail screens (no tab bar) pass this to show a back arrow next to the title. */
  onBack?: () => void;
  /** Pull-to-refresh. Every list screen passes this. */
  onRefresh?: () => void;
  refreshing?: boolean;
  /** Edges to pad for. Screens inside the tab bar leave the bottom to the tab bar. */
  edges?: ('top' | 'bottom' | 'left' | 'right')[];
  contentStyle?: StyleProp<ViewStyle>;
  /** Pinned below the scrolling content (e.g. a sticky action bar). */
  footer?: ReactNode;
}

/** Safe-area-aware scrolling page with the standard gutter and an optional title. */
export function Screen({ title, subtitle, children, onBack, onRefresh, refreshing = false, edges = ['top', 'left', 'right'], contentStyle, footer }: ScreenProps) {
  return (
    <SafeAreaView style={styles.safe} edges={edges}>
      <ScrollView
        contentContainerStyle={[styles.content, contentStyle]}
        keyboardShouldPersistTaps="handled"
        refreshControl={onRefresh ? <RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={colors.primary} colors={[colors.primary]} /> : undefined}
      >
        {title || onBack ? (
          <View style={styles.header}>
            {onBack ? (
              <Pressable onPress={onBack} accessibilityRole="button" accessibilityLabel="Go back" hitSlop={8} style={styles.back}>
                <Icon name="arrow-left" size={22} color={colors.ink} />
              </Pressable>
            ) : null}
            <View style={styles.titles}>
              {title ? (
                <Text variant="title" accessibilityRole="header">
                  {title}
                </Text>
              ) : null}
              {subtitle ? <Text variant="small">{subtitle}</Text> : null}
            </View>
          </View>
        ) : null}
        {children}
      </ScrollView>
      {footer}
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  content: { padding: spacing.lg, gap: spacing.md, flexGrow: 1 },
  header: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, paddingBottom: spacing.sm },
  back: { width: MIN_TOUCH, height: MIN_TOUCH, marginLeft: -spacing.sm, alignItems: 'center', justifyContent: 'center' },
  titles: { flex: 1, gap: 2 },
});
