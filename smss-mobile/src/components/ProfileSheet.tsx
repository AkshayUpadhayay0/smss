import Constants from 'expo-constants';
import { Alert, Modal, Pressable, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useSession } from '../auth/session';
import { colors, radii, spacing } from '../theme/tokens';
import { Avatar } from './Avatar';
import { Button } from './Button';
import { Icon, type IconName } from './Icon';
import { Text } from './Text';

function InfoRow({ icon, label, value }: { icon: IconName; label: string; value: string }) {
  return (
    <View style={styles.infoRow} accessible accessibilityLabel={`${label}: ${value}`}>
      <Icon name={icon} size={18} color={colors.primaryText} />
      <View style={{ flex: 1 }}>
        <Text variant="small">{label}</Text>
        <Text variant="body" color={colors.ink} weight="medium">
          {value}
        </Text>
      </View>
    </View>
  );
}

export interface ProfileSheetProps {
  visible: boolean;
  onClose: () => void;
  /** Students: "Class 5 · A". Shown only when given. */
  classText?: string | null;
  /** When set, Sign out first warns with this text (the driver app: signing out ends an open duty). */
  signOutWarning?: string | null;
  /** Runs after the driver confirms Sign out and before the session ends (the driver app ends duty here). Never blocks sign-out when it fails. */
  beforeSignOut?: () => Promise<void>;
}

/**
 * The ONE profile sheet (both Homes open it from the avatar): name, role, school, class/section (students), app version, and Sign out behind a
 * confirmation. A Change password entry is intentionally not here yet.
 */
export function ProfileSheet({ visible, onClose, classText, signOutWarning, beforeSignOut }: ProfileSheetProps) {
  const { user, role, signOut } = useSession();
  const insets = useSafeAreaInsets();
  const version = Constants.expoConfig?.version ?? '';

  function confirmSignOut() {
    Alert.alert('Sign out?', signOutWarning ?? 'You will need to sign in again to use the app.', [
      { text: 'Cancel', style: 'cancel' },
      {
        text: signOutWarning ? 'End duty and sign out' : 'Sign out',
        style: 'destructive',
        onPress: () => {
          onClose();
          void (async () => {
            try {
              await beforeSignOut?.();
            } catch {
              // ending duty is best effort: the driver can always sign out
            }
            await signOut();
          })();
        },
      },
    ]);
  }

  return (
    <Modal visible={visible} transparent animationType="slide" onRequestClose={onClose} statusBarTranslucent>
      <View style={styles.root}>
        <Pressable style={styles.backdrop} onPress={onClose} accessibilityRole="button" accessibilityLabel="Close profile" />
        <View style={[styles.sheet, { paddingBottom: insets.bottom + spacing.lg }]} accessibilityViewIsModal>
          <View style={styles.handle} />
          <View style={styles.header}>
            <Avatar name={user?.displayName} size={64} />
            <View style={{ flex: 1, gap: 2 }}>
              <Text variant="title" accessibilityRole="header">
                {user?.displayName}
              </Text>
              <Text variant="small">{role === 'STUDENT' ? 'Student' : role === 'TEACHER' ? 'Teacher' : role === 'DRIVER' ? 'Driver' : ''}</Text>
            </View>
            <Pressable onPress={onClose} hitSlop={8} accessibilityRole="button" accessibilityLabel="Close" style={styles.close}>
              <Icon name="x" size={22} color={colors.inkSoft} />
            </Pressable>
          </View>

          <View style={styles.info}>
            {user?.schoolName ? <InfoRow icon="home" label="School" value={user.schoolName} /> : null}
            {classText ? <InfoRow icon="users" label="Class" value={classText} /> : null}
          </View>

          <Button title="Sign out" icon="log-out" variant="secondary" onPress={confirmSignOut} />
          {version ? (
            <Text variant="small" style={styles.version}>
              SMSS app version {version}
            </Text>
          ) : null}
        </View>
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, justifyContent: 'flex-end' },
  backdrop: { ...StyleSheet.absoluteFill, backgroundColor: 'rgba(30,27,46,0.45)' },
  sheet: { backgroundColor: colors.surface, borderTopLeftRadius: 28, borderTopRightRadius: 28, padding: spacing.lg, gap: spacing.lg },
  handle: { alignSelf: 'center', width: 40, height: 4, borderRadius: 2, backgroundColor: colors.border },
  header: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  close: { width: 44, height: 44, alignItems: 'center', justifyContent: 'center' },
  info: { gap: spacing.md, backgroundColor: colors.background, borderRadius: radii.card, padding: spacing.lg, borderWidth: 1, borderColor: colors.divider },
  infoRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  version: { textAlign: 'center' },
});
