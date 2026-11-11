import type { ReactNode } from 'react';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useSession } from '../auth/session';
import { Avatar } from './Avatar';
import { ProfileSheet } from './ProfileSheet';
import { Text } from './Text';

/** The top of both Homes: avatar (opens the shared profile sheet), greeting, and an optional trailing control (the student's bell). */
export function HomeHeader({ subtitle, classText, trailing }: { subtitle?: string | null; classText?: string | null; trailing?: ReactNode }) {
  const { user } = useSession();
  const [open, setOpen] = useState(false);
  const first = (user?.displayName ?? '').trim().split(/\s+/)[0] || 'there';
  return (
    <View style={styles.row}>
      <Pressable
        onPress={() => setOpen(true)}
        accessibilityRole="button"
        accessibilityLabel="Open your profile"
        style={styles.who}
      >
        <Avatar name={user?.displayName} />
        <View style={styles.text}>
          <Text variant="heading" numberOfLines={1}>
            Hello, {first}
          </Text>
          {subtitle ? (
            <Text variant="small" numberOfLines={1}>
              {subtitle}
            </Text>
          ) : null}
        </View>
      </Pressable>
      {trailing}
      <ProfileSheet visible={open} onClose={() => setOpen(false)} classText={classText} />
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  who: { flex: 1, flexDirection: 'row', alignItems: 'center', gap: 12, minHeight: 48 },
  text: { flex: 1, gap: 2 },
});
