import { useEffect, useState } from 'react';
import { Animated, StyleSheet, View, type DimensionValue } from 'react-native';
import { colors, radii } from '../theme/tokens';

/** A pulsing placeholder block. Hidden from screen readers; the screen announces loading through its own label. */
export function Skeleton({ width = '100%', height = 16, radius = 8 }: { width?: DimensionValue; height?: number; radius?: number }) {
  const [opacity] = useState(() => new Animated.Value(0.5));
  useEffect(() => {
    const loop = Animated.loop(
      Animated.sequence([
        Animated.timing(opacity, { toValue: 1, duration: 700, useNativeDriver: true }),
        Animated.timing(opacity, { toValue: 0.5, duration: 700, useNativeDriver: true }),
      ]),
    );
    loop.start();
    return () => loop.stop();
  }, [opacity]);
  return <Animated.View importantForAccessibility="no-hide-descendants" style={{ width, height, borderRadius: radius, backgroundColor: colors.divider, opacity }} />;
}

/** A card-shaped skeleton for list screens: show a few while loading. */
export function SkeletonCard() {
  return (
    <View style={styles.card} accessibilityLabel="Loading">
      <Skeleton width="40%" height={14} />
      <Skeleton width="85%" height={18} />
      <Skeleton width="60%" height={14} />
    </View>
  );
}

const styles = StyleSheet.create({
  card: { gap: 10, padding: 16, borderRadius: radii.card, borderWidth: 1, borderColor: colors.border, backgroundColor: colors.surface },
});
