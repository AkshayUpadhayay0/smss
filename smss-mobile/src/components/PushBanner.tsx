import { useEffect } from 'react';
import { Pressable, StyleSheet } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { dismissBanner, useBanner } from '../services/push/banner';
import { colors, radii, shadow } from '../theme/tokens';
import { Text } from './Text';

const SHOW_MS = 6_000;

/** The in-app banner for a push that arrives while the app is open (FCM draws nothing in the foreground). Tap or wait to dismiss. */
export function PushBanner() {
  const banner = useBanner();
  const insets = useSafeAreaInsets();

  useEffect(() => {
    if (!banner) return;
    const t = setTimeout(dismissBanner, SHOW_MS);
    return () => clearTimeout(t);
  }, [banner]);

  if (!banner) return null;
  return (
    <Pressable
      onPress={dismissBanner}
      accessibilityRole="alert"
      accessibilityLiveRegion="polite"
      accessibilityLabel={[banner.title, banner.body].filter(Boolean).join('. ')}
      style={[styles.banner, { top: insets.top + 8 }]}
    >
      {banner.title ? <Text weight="semibold" color={colors.ink}>{banner.title}</Text> : null}
      {banner.body ? <Text variant="small" color={colors.inkSoft}>{banner.body}</Text> : null}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  banner: {
    position: 'absolute',
    left: 12,
    right: 12,
    zIndex: 1000,
    backgroundColor: colors.surface,
    borderRadius: radii.card,
    borderWidth: 1,
    borderColor: colors.tintBorder,
    padding: 14,
    gap: 2,
    ...shadow.card,
  },
});
