import { useRouter } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Screen } from '../../components/Screen';
import { Text } from '../../components/Text';
import { colors, spacing } from '../../theme/tokens';
import { BATTERY_HINTS } from './copy';
import { openBatterySettings } from './permissionsNative';

/**
 * Battery guidance. The button only opens Android's own battery-optimisation list: SMSS never asks to be exempted itself
 * (it does not hold REQUEST_IGNORE_BATTERY_OPTIMIZATIONS). The driver chooses; the notes below say where the usual switches are.
 */
export function BatteryScreen() {
  const router = useRouter();
  return (
    <Screen title="Keep sharing during your trip" subtitle="Battery settings" onBack={() => router.back()} contentStyle={{ gap: spacing.lg }}>
      <Card style={{ gap: spacing.sm }}>
        <Text variant="body" color={colors.ink}>
          Some phones stop apps that run in the background to save battery. If that happens during a trip your school sees your vehicle go offline.
        </Text>
        <Text variant="small">{'Set SMSS to "Unrestricted" (or "No restrictions") in your phone\'s battery settings.'}</Text>
      </Card>

      <Button title="Open battery settings" icon="battery-charging" onPress={() => void openBatterySettings()} />

      <Text variant="heading">By phone brand</Text>
      <View style={{ gap: spacing.sm }}>
        {BATTERY_HINTS.map((h) => (
          <Card key={h.maker} style={styles.hint}>
            <Text variant="label" color={colors.ink} weight="semibold">
              {h.maker}
            </Text>
            <Text variant="small">{h.hint}</Text>
          </Card>
        ))}
      </View>
      <Text variant="small">{'Menu names change between Android versions. If you cannot find these, search "battery" in Settings.'}</Text>
      <Button title="Done" variant="secondary" onPress={() => router.back()} />
    </Screen>
  );
}

const styles = StyleSheet.create({
  hint: { gap: 4 },
});
