import { useQueryClient } from '@tanstack/react-query';
import { Redirect, useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Chip } from '../../components/Chip';
import { Screen } from '../../components/Screen';
import { Text } from '../../components/Text';
import { colors, spacing } from '../../theme/tokens';
import { guideFor, nextStep, STEP_ORDER, notificationsRequired, type PermKey, type PermSnapshot } from './permissions';
import { androidApi, openAppSettings, requestStep } from './permissionsNative';
import { driverKeys, useDisclosureAccepted, usePermissions } from './queries';

const LABEL: Record<PermKey, string> = { foreground: 'Location while using the app', background: 'Location all the time', notifications: 'Notifications' };

function Progress({ perms }: { perms: PermSnapshot }) {
  return (
    <View style={styles.progress}>
      {STEP_ORDER.filter((k) => k !== 'notifications' || notificationsRequired(androidApi)).map((k) => (
        <View key={k} style={styles.progressRow}>
          <Text variant="small" color={colors.ink} style={{ flex: 1 }}>
            {LABEL[k]}
          </Text>
          <Chip label={perms[k] === 'granted' ? 'Allowed' : 'Needed'} tone={perms[k] === 'granted' ? 'success' : 'warning'} icon={perms[k] === 'granted' ? 'check' : 'alert-circle'} />
        </View>
      ))}
    </View>
  );
}

/**
 * The permission steps, one at a time, each with a plain explanation and an exact "what to tap". Reachable only after the disclosure was accepted.
 * Denied: ask again. Denied forever (or background location on Android 11+): the button opens the app's Settings page. When the driver comes back
 * from Settings the state is re-read automatically (usePermissions listens for the app becoming active).
 */
export function PermissionsScreen() {
  const router = useRouter();
  const qc = useQueryClient();
  const disclosure = useDisclosureAccepted();
  const perms = usePermissions();
  const [busy, setBusy] = useState(false);

  if (disclosure.data === false) return <Redirect href={'/(driver)/disclosure' as Href} />;
  if (disclosure.isPending || perms.isPending || !perms.data) return <Screen title="Location sharing" subtitle="Checking permissions…"><View /></Screen>;

  const snapshot = perms.data;
  const step = nextStep(snapshot, androidApi);

  async function onAction(current: PermKey, action: 'request' | 'openSettings') {
    setBusy(true);
    try {
      if (action === 'openSettings') await openAppSettings();
      else await requestStep(current);
    } finally {
      await qc.invalidateQueries({ queryKey: driverKeys.permissions });
      setBusy(false);
    }
  }

  if (!step) {
    return (
      <Screen title="You are all set" subtitle="Location sharing is allowed" contentStyle={{ gap: spacing.lg }}>
        <Progress perms={snapshot} />
        <Card style={{ gap: spacing.sm }}>
          <Text variant="body" color={colors.ink} weight="medium">
            One more thing: battery settings
          </Text>
          <Text variant="small">Some phones stop apps in the background to save battery. A quick check makes sure SMSS keeps sharing during your trip.</Text>
        </Card>
        <View style={{ gap: spacing.md }}>
          <Button title="Check battery settings" icon="battery-charging" variant="secondary" onPress={() => router.push('/(driver)/battery' as Href)} />
          <Button title="Done" onPress={() => router.replace('/(driver)' as Href)} />
        </View>
      </Screen>
    );
  }

  const state = snapshot[step];
  const guide = guideFor(step, state, androidApi);
  return (
    <Screen title={guide.title} contentStyle={{ gap: spacing.lg }}>
      <Progress perms={snapshot} />
      <Card style={{ gap: spacing.md }}>
        <Text variant="body" color={colors.ink}>
          {guide.why}
        </Text>
        <View style={styles.tapBox} accessible accessibilityLabel={`What to tap. ${guide.tap}`}>
          <Text variant="small" weight="semibold" color={colors.primaryText}>
            What to tap
          </Text>
          <Text variant="small" color={colors.ink}>
            {guide.tap}
          </Text>
        </View>
        {guide.retryNote ? (
          <Text variant="small" color={colors.danger} accessibilityRole="alert">
            {guide.retryNote}
          </Text>
        ) : null}
      </Card>
      <View style={{ gap: spacing.md }}>
        <Button title={guide.actionLabel} icon={guide.action === 'openSettings' ? 'settings' : 'check'} loading={busy} onPress={() => void onAction(step, guide.action)} />
        <Button title="Not now" variant="secondary" onPress={() => router.replace('/(driver)' as Href)} />
      </View>
      {guide.action === 'openSettings' ? <Text variant="small">After you change it, come back to SMSS: this screen updates by itself.</Text> : null}
    </Screen>
  );
}

const styles = StyleSheet.create({
  progress: { gap: spacing.sm },
  progressRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  tapBox: { backgroundColor: colors.tint, borderRadius: 12, padding: spacing.md, gap: 4, borderWidth: 1, borderColor: colors.tintBorder },
});
