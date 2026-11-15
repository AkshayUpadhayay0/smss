import { useQueryClient } from '@tanstack/react-query';
import { useRouter, type Href } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Icon } from '../../components/Icon';
import { Screen } from '../../components/Screen';
import { Text } from '../../components/Text';
import { colors, spacing } from '../../theme/tokens';
import { DISCLOSURE_TEXT } from './copy';
import { dutyStore } from './nativeStore';
import { skipSetupThisRun } from './setupState';
import { driverKeys } from './queries';

/**
 * The prominent disclosure. Shown BEFORE any permission prompt (Google Play requirement): nothing on this screen or before it asks Android for anything.
 * Continue records the acceptance and opens the permission steps; Not now returns to Home, where duty cannot start until this is done.
 */
export function DisclosureScreen() {
  const router = useRouter();
  const qc = useQueryClient();

  async function onContinue() {
    await dutyStore.acceptDisclosure();
    await qc.invalidateQueries({ queryKey: driverKeys.disclosure });
    router.replace('/(driver)/permissions' as Href);
  }

  function onNotNow() {
    skipSetupThisRun();
    router.replace('/(driver)' as Href);
  }

  return (
    <Screen title="Before you share your location" contentStyle={{ gap: spacing.lg }}>
      <Card style={styles.card}>
        <View style={styles.icon}>
          <Icon name="map-pin" size={28} color={colors.primaryText} />
        </View>
        <Text variant="body" color={colors.ink} accessibilityRole="text">
          {DISCLOSURE_TEXT}
        </Text>
      </Card>
      <Text variant="small">The next screens ask Android for permission, one at a time. Nothing is shared until you start duty.</Text>
      <View style={{ gap: spacing.md }}>
        <Button title="Continue" icon="arrow-right" onPress={() => void onContinue()} />
        <Button title="Not now" variant="secondary" onPress={onNotNow} />
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  card: { gap: spacing.md },
  icon: { width: 52, height: 52, borderRadius: 26, backgroundColor: colors.tint, alignItems: 'center', justifyContent: 'center' },
});

