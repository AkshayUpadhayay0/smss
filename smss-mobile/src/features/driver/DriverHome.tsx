import { useQueryClient } from '@tanstack/react-query';
import { Redirect, useRouter, type Href } from 'expo-router';
import { useEffect, useState } from 'react';
import { Alert, StyleSheet, View } from 'react-native';
import { messageOf } from '../../api/errors';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Chip } from '../../components/Chip';
import { HomeHeader } from '../../components/HomeHeader';
import { Icon } from '../../components/Icon';
import { QueryState } from '../../components/QueryState';
import { Screen } from '../../components/Screen';
import { Text } from '../../components/Text';
import { colors, spacing } from '../../theme/tokens';
import { SIGN_OUT_ON_DUTY_WARNING, STOP_MESSAGES } from './copy';
import { endDuty, startDuty, stopSharing } from './duty';
import { deriveDutyView, formatClock, lastSentText, startBlockedMessage, STATUS_HELP, STATUS_TEXT, type DutyStatus } from './dutyView';
import { dutyStore } from './nativeStore';
import { canShareLocation, gateFor } from './permissions';
import { androidApi } from './permissionsNative';
import { driverKeys, useDisclosureAccepted, useDriverMe, usePermissions, useServiceRunning, useStoppedReason } from './queries';
import { setupSkipped } from './setupState';

const STATUS_TONE: Record<DutyStatus, 'success' | 'warning' | 'neutral'> = { LIVE: 'success', STALE: 'warning', OFFLINE: 'neutral' };

export function DriverHome() {
  const router = useRouter();
  const qc = useQueryClient();
  const me = useDriverMe();
  const running = useServiceRunning();
  const stopped = useStoppedReason();
  const perms = usePermissions();
  const disclosure = useDisclosureAccepted();
  const [busy, setBusy] = useState<'start' | 'end' | null>(null);

  const d = me.data;
  const serverOnDuty = !!d?.duty.onDuty;
  const serviceRunning = !!running.data;
  const view = deriveDutyView({ serverOnDuty, serviceRunning, stopped: stopped.data ?? null });
  const gate = disclosure.data !== undefined && perms.data ? gateFor(disclosure.data, perms.data, androidApi) : null;
  const canShare = !!perms.data && canShareLocation(perms.data, androidApi);

  // The server says off duty but the service still runs (the school ended it, or a previous run left it): stop it so nothing keeps sending.
  useEffect(() => {
    if (d && !d.duty.onDuty && serviceRunning && busy === null) {
      void stopSharing().then(() => qc.invalidateQueries({ queryKey: driverKeys.sharing }));
    }
  }, [d, serviceRunning, busy, qc]);

  const refreshAll = async () => {
    await Promise.all([me.refetch(), running.refetch(), stopped.refetch(), perms.refetch()]);
  };

  // First run: the disclosure comes first, before any permission prompt. "Not now" skips it for this run only.
  if (disclosure.data === false && !setupSkipped()) return <Redirect href={'/(driver)/disclosure' as Href} />;

  async function onStart() {
    setBusy('start');
    try {
      await startDuty();
    } catch (e) {
      Alert.alert('Could not start duty', messageOf(e)); // the server's own words for a 409 ("No vehicle is assigned to you...")
    } finally {
      await refreshAll();
      setBusy(null);
    }
  }

  function onEnd() {
    Alert.alert('End duty?', 'Your school will stop seeing this vehicle until you start duty again.', [
      { text: 'Keep sharing', style: 'cancel' },
      {
        text: 'End duty',
        style: 'destructive',
        onPress: () => {
          void (async () => {
            setBusy('end');
            try {
              const { serverEnded } = await endDuty();
              if (!serverEnded) {
                Alert.alert('Duty ended on this phone', 'Sharing has stopped, but your school could not be told right now. They will see your vehicle go offline.');
              }
            } finally {
              await refreshAll();
              setBusy(null);
            }
          })();
        },
      },
    ]);
  }

  async function onAcknowledge() {
    await dutyStore.clearStopped();
    await qc.invalidateQueries({ queryKey: driverKeys.stopped });
  }

  const blockedMessage = startBlockedMessage(d, perms.data ?? null, androidApi);
  const onDutyView = view.kind === 'SHARING' || view.kind === 'NOT_SHARING';
  const since = formatClock(d?.duty.startedAt);

  return (
    <Screen onRefresh={() => void refreshAll()} refreshing={me.isRefetching && busy === null}>
      <HomeHeader
        subtitle="Driver"
        signOutWarning={onDutyView || serviceRunning ? SIGN_OUT_ON_DUTY_WARNING : null}
        beforeSignOut={async () => {
          await endDuty();
        }}
      />

      <QueryState loading={me.isPending} error={me.error} onRetry={() => void me.refetch()} skeletons={3}>
        {d ? (
          <>
            <Card style={{ gap: spacing.sm }}>
              <View style={styles.row}>
                <Icon name="truck" size={20} color={colors.primaryText} />
                <Text variant="label" weight="semibold" color={colors.ink}>
                  Your vehicle
                </Text>
              </View>
              {d.vehicle ? (
                <>
                  <Text variant="title" color={colors.ink}>
                    {d.vehicle.vehicleLabel}
                  </Text>
                  <Text variant="body">{d.vehicle.vehiclePlate}</Text>
                  {!d.vehicle.isActive ? <Chip label="Vehicle inactive" tone="warning" icon="alert-circle" /> : null}
                </>
              ) : (
                <Text variant="body">No vehicle is assigned to you yet. Ask your school to assign one.</Text>
              )}
              <View style={[styles.row, { marginTop: spacing.sm }]}>
                <Icon name="map-pin" size={18} color={colors.primaryText} />
                <Text variant="small" weight="semibold" color={colors.ink}>
                  Your areas
                </Text>
              </View>
              <Text variant="body">{d.areas.length ? d.areas.map((a) => a.areaName).join(', ') : 'No areas assigned.'}</Text>
            </Card>

            <Card style={{ gap: spacing.sm }} accessibilityLiveRegion="polite">
              <View style={styles.row}>
                <Icon name="clock" size={20} color={colors.primaryText} />
                <Text variant="label" weight="semibold" color={colors.ink}>
                  Duty
                </Text>
                <View style={{ flex: 1 }} />
                <Chip label={onDutyView ? 'On duty' : 'Off duty'} tone={onDutyView ? 'success' : 'neutral'} icon={onDutyView ? 'radio' : 'moon'} />
              </View>

              {view.kind === 'OFF' ? <Text variant="body">You are off duty. Your school cannot see this vehicle.</Text> : null}

              {onDutyView ? (
                <>
                  <Text variant="body" color={colors.ink}>
                    {since ? `On duty since ${since}` : 'On duty'}
                  </Text>
                  {d.duty.status ? (
                    <View style={{ gap: 4 }}>
                      <Chip label={STATUS_TEXT[d.duty.status]} tone={STATUS_TONE[d.duty.status]} />
                      <Text variant="small">{STATUS_HELP[d.duty.status]}</Text>
                    </View>
                  ) : null}
                  <Text variant="small">{lastSentText(d.duty.lastSentAgeSeconds)}</Text>
                </>
              ) : null}

              {view.kind === 'NOT_SHARING' ? (
                <View style={styles.notice} accessibilityRole="alert">
                  <Text variant="label" weight="semibold" color={colors.warningText}>
                    Location sharing stopped. Resume?
                  </Text>
                  <Text variant="small" color={colors.warningText}>
                    Your duty is still open, but this phone is not sending its location (the app or phone may have restarted).
                  </Text>
                </View>
              ) : null}

              {view.kind === 'ENDED' ? (
                <View style={styles.notice} accessibilityRole="alert">
                  <Text variant="label" weight="semibold" color={colors.warningText}>
                    Duty ended
                  </Text>
                  <Text variant="small" color={colors.warningText}>
                    {STOP_MESSAGES[view.reason]}
                  </Text>
                  <Button title="OK" variant="secondary" onPress={() => void onAcknowledge()} />
                </View>
              ) : null}
            </Card>

            {view.kind === 'SHARING' ? (
              <Button title="End duty" icon="square" variant="danger" loading={busy === 'end'} disabled={busy !== null} onPress={onEnd} />
            ) : view.kind === 'NOT_SHARING' ? (
              <View style={{ gap: spacing.md }}>
                <Button title="Resume sharing" icon="play" loading={busy === 'start'} disabled={busy !== null || !canShare} onPress={() => void onStart()} />
                <Button title="End duty" icon="square" variant="danger" loading={busy === 'end'} disabled={busy !== null} onPress={onEnd} />
              </View>
            ) : (
              <View style={{ gap: spacing.sm }}>
                <Button
                  title="Mark today's attendance"
                  icon="check-circle"
                  loading={busy === 'start'}
                  disabled={busy !== null || blockedMessage !== null}
                  onPress={() => void onStart()}
                />
                {blockedMessage ? (
                  <Text variant="small" color={colors.warningText} accessibilityRole="alert">
                    {blockedMessage}
                  </Text>
                ) : (
                  <Text variant="small">{"Starts your duty and shares this vehicle's location until you tap End duty."}</Text>
                )}
              </View>
            )}

            {gate && gate !== 'ready' ? (
              <Button
                title="Set up location sharing"
                icon="settings"
                variant="secondary"
                onPress={() => router.push((gate === 'disclosure' ? '/(driver)/disclosure' : '/(driver)/permissions') as Href)}
              />
            ) : (
              <Button title="Battery settings help" icon="battery-charging" variant="ghost" onPress={() => router.push('/(driver)/battery' as Href)} />
            )}
          </>
        ) : null}
      </QueryState>
    </Screen>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  notice: { backgroundColor: colors.warningTint, borderRadius: 12, padding: spacing.md, gap: spacing.sm },
});
