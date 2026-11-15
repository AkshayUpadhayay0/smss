import { useFocusEffect, useRouter } from 'expo-router';
import { useCallback, useEffect, useMemo, useRef, useState, type ComponentType, type RefAttributes } from 'react';
import { Linking, Pressable, StyleSheet, View } from 'react-native';
import { WebView as NativeWebView, type WebViewNavigation, type WebViewProps } from 'react-native-webview';
import { messageOf } from '../../../api/errors';
import { Button } from '../../../components/Button';
import { Card } from '../../../components/Card';
import { Chip, type ChipTone } from '../../../components/Chip';
import { EmptyState } from '../../../components/EmptyState';
import { ErrorState } from '../../../components/ErrorState';
import { Icon } from '../../../components/Icon';
import { Screen } from '../../../components/Screen';
import { Skeleton } from '../../../components/Skeleton';
import { Text } from '../../../components/Text';
import { MAP_CONFIG } from '../../../config/map';
import { colors, radii, spacing } from '../../../theme/tokens';
import { useMyBus } from '../queries';
import { buildUpdateScript, deriveBusState, mapPoints, type BusCard, type ScheduledCard } from './busView';
import { buildMapPage } from './mapPage';

// react-native-webview's class typing does not line up with React 19's JSX types: give it the plain component shape it really has.
const WebView = NativeWebView as unknown as ComponentType<WebViewProps & RefAttributes<NativeWebView>>;

const TONE: Record<BusCard['status'], ChipTone> = { LIVE: 'success', STALE: 'warning', OFFLINE: 'neutral' };

/** The approximate times of the student's area, one line each (nothing when the school has not set any). */
function TimeLines({ pickup, drop }: { pickup: string | null; drop: string | null }) {
  if (!pickup && !drop) return null;
  return (
    <View style={{ gap: 2 }}>
      {pickup ? <Text variant="small">{pickup}</Text> : null}
      {drop ? <Text variant="small">{drop}</Text> : null}
    </View>
  );
}

/** Drivers of the area who are not on duty now but have approximate times: the schedule, never a position. */
function ScheduledList({ items }: { items: ScheduledCard[] }) {
  if (items.length === 0) return null;
  return (
    <View style={{ gap: spacing.md }}>
      <Text variant="heading">Not on duty now</Text>
      {items.map((s) => (
        <Card
          key={s.key}
          style={{ gap: 6 }}
          accessible
          accessibilityLabel={`${s.vehicleLabel}, ${s.vehiclePlate}, driver ${s.driverName}, not on duty${s.pickupText ? ', ' + s.pickupText : ''}${s.dropText ? ', ' + s.dropText : ''}`}
        >
          <View style={styles.row}>
            <Icon name="truck" size={20} color={colors.primaryText} />
            <Text variant="heading" style={{ flex: 1 }} numberOfLines={1}>
              {s.vehicleLabel}
            </Text>
            <Chip label="Not on duty" tone="neutral" />
          </View>
          <Text variant="small">
            {s.vehiclePlate} · Driver {s.driverName}
          </Text>
          <TimeLines pickup={s.pickupText} drop={s.dropText} />
        </Card>
      ))}
    </View>
  );
}

/** The map page is built once: Leaflet is inlined and the page never changes, only its markers (through injectJavaScript). */
const PAGE = buildMapPage({
  tileUrl: MAP_CONFIG.tileUrl,
  attribution: MAP_CONFIG.attribution,
  center: MAP_CONFIG.defaultCenter,
  zoom: MAP_CONFIG.defaultZoom,
  maxZoom: MAP_CONFIG.maxZoom,
  focusZoom: MAP_CONFIG.focusZoom,
});

/**
 * "Where is my bus". Only the buses on duty for the student's own area are shown (the server decides; this screen never names a student or an area).
 * Polled every 20 s while the screen is focused and the app is in the foreground. The map is a WebView with Leaflet bundled in the app: only the tiles come from the network,
 * and they carry the Referer set through the WebView's baseUrl (see src/config/map.ts).
 */
export function MyBusScreen() {
  const router = useRouter();
  const [focused, setFocused] = useState(true);
  useFocusEffect(
    useCallback(() => {
      setFocused(true);
      return () => setFocused(false); // not focused: stop polling
    }, []),
  );

  const query = useMyBus(focused ? MAP_CONFIG.pollSeconds : false);
  const state = useMemo(() => (query.data ? deriveBusState(query.data) : null), [query.data]);
  const points = useMemo(() => (query.data ? mapPoints(query.data.buses) : []), [query.data]);

  const web = useRef<NativeWebView>(null);
  const [mapReady, setMapReady] = useState(false);
  const script = useMemo(() => buildUpdateScript(points), [points]);
  // move the markers whenever the answer changes, and once when the page has finished loading
  useEffect(() => {
    if (mapReady) web.current?.injectJavaScript(script);
  }, [script, mapReady]);

  // the only link on the page is the OpenStreetMap credit: it opens in the browser; the map page itself never navigates away
  const onShouldStart = (r: WebViewNavigation) => {
    if (r.url === 'about:blank' || r.url.startsWith(MAP_CONFIG.baseUrl)) return true;
    if (/^https:\/\//i.test(r.url)) void Linking.openURL(r.url);
    return false;
  };

  const showMap = state?.kind === 'BUSES' && points.length > 0;

  return (
    <Screen title="Where is my bus" subtitle={state && state.kind !== 'NOT_ASSIGNED' ? state.areaName : undefined} onBack={() => router.back()} onRefresh={() => void query.refetch()} refreshing={query.isRefetching}>
      {query.isPending ? (
        <View style={{ gap: spacing.md }} accessibilityLabel="Loading" accessibilityLiveRegion="polite">
          <Skeleton height={260} />
          <Skeleton height={90} />
        </View>
      ) : query.isError && !query.data ? (
        <ErrorState message={messageOf(query.error)} onRetry={() => void query.refetch()} />
      ) : state?.kind === 'NOT_ASSIGNED' ? (
        <EmptyState icon="map-pin" title="No bus assigned" message={state.message} />
      ) : state?.kind === 'NO_BUS' ? (
        <>
          <EmptyState icon="clock" title="No bus on duty" message={state.message} />
          <ScheduledList items={state.scheduled} />
        </>
      ) : state?.kind === 'BUSES' ? (
        <>
          {query.isError ? (
            <Card style={styles.notice} accessibilityRole="alert">
              <Icon name="alert-triangle" size={18} color={colors.warningText} />
              <View style={{ flex: 1 }}>
                <Text variant="small" color={colors.warningText}>
                  Could not refresh. Showing the last answer.
                </Text>
              </View>
              <Button title="Retry" variant="secondary" onPress={() => void query.refetch()} />
            </Card>
          ) : null}

          {showMap ? (
            <View style={styles.mapWrap} accessible accessibilityLabel="Map showing where your school bus is">
              <WebView
                ref={web}
                style={styles.map}
                originWhitelist={['*']}
                source={{ html: PAGE, baseUrl: MAP_CONFIG.baseUrl }}
                javaScriptEnabled
                domStorageEnabled={false}
                setSupportMultipleWindows={false}
                scrollEnabled={false}
                onLoadEnd={() => setMapReady(true)}
                onShouldStartLoadWithRequest={onShouldStart}
                androidLayerType="hardware"
              />
            </View>
          ) : (
            <Card style={styles.notice}>
              <Icon name="map" size={18} color={colors.muted} />
              <View style={{ flex: 1 }}>
                <Text variant="small">There is no position to show on the map right now.</Text>
              </View>
            </Card>
          )}

          {state.buses.map((b) => (
            <Card key={b.key} style={{ gap: 6 }} accessible accessibilityLabel={`${b.vehicleLabel}, ${b.vehiclePlate}, driver ${b.driverName}, ${b.statusText}${b.updatedText ? ', ' + b.updatedText : ''}${b.pickupText ? ', ' + b.pickupText : ''}${b.dropText ? ', ' + b.dropText : ''}`}>
              <View style={styles.row}>
                <Icon name="truck" size={20} color={colors.primaryText} />
                <Text variant="heading" style={{ flex: 1 }} numberOfLines={1}>
                  {b.vehicleLabel}
                </Text>
                <Chip label={b.statusText} tone={TONE[b.status]} />
              </View>
              <Text variant="small">
                {b.vehiclePlate} · Driver {b.driverName}
              </Text>
              {b.updatedText ? (
                <Text variant="label" color={colors.ink}>
                  {b.updatedText}
                </Text>
              ) : null}
              <TimeLines pickup={b.pickupText} drop={b.dropText} />
              {b.warning ? (
                <View style={styles.warning} accessibilityRole="alert">
                  <Text variant="small" color={colors.warningText}>
                    {b.warning}
                  </Text>
                </View>
              ) : null}
            </Card>
          ))}

          <ScheduledList items={state.scheduled} />

          <Pressable onPress={() => void Linking.openURL('https://www.openstreetmap.org/copyright')} accessibilityRole="link" accessibilityLabel="Map data from OpenStreetMap contributors" style={styles.credit}>
            <Text variant="small">Map data © OpenStreetMap contributors</Text>
          </Pressable>
        </>
      ) : null}
    </Screen>
  );
}

const styles = StyleSheet.create({
  mapWrap: { height: 320, borderRadius: radii.card, overflow: 'hidden', borderWidth: 1, borderColor: colors.border, backgroundColor: colors.tint },
  map: { flex: 1, backgroundColor: colors.tint },
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  notice: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  warning: { backgroundColor: colors.warningTint, borderRadius: 12, padding: spacing.md },
  credit: { alignSelf: 'center', minHeight: 44, justifyContent: 'center' },
});
