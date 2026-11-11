import type { ComponentProps } from 'react';
import { StyleSheet, View } from 'react-native';
import { Tabs } from 'expo-router';
import { colors, fonts, radii } from '../theme/tokens';
import { Icon, type IconName } from './Icon';

type TabsProps = ComponentProps<typeof Tabs>;

/** Shared bottom tab bar look: 5 tabs, a tint pill behind the active icon, 11px / 600 labels. */
export const tabScreenOptions: TabsProps['screenOptions'] = {
  headerShown: false,
  tabBarActiveTintColor: colors.primaryText,
  tabBarInactiveTintColor: colors.muted,
  tabBarLabelStyle: { fontFamily: fonts.semibold, fontSize: 11 },
  tabBarStyle: { backgroundColor: colors.surface, borderTopColor: colors.border, borderTopWidth: 1 },
};

/** The `tabBarIcon` for one tab. The pill is the active indicator (the label colour changes too, so it is not colour alone). */
export function tabIcon(name: IconName) {
  return function TabIcon({ focused }: { focused: boolean }) {
    return (
      <View style={[styles.pill, focused && styles.pillActive]}>
        <Icon name={name} size={20} color={focused ? colors.primaryText : colors.muted} />
      </View>
    );
  };
}

const styles = StyleSheet.create({
  pill: { width: 52, height: 30, borderRadius: radii.pill, alignItems: 'center', justifyContent: 'center' },
  pillActive: { backgroundColor: colors.tint },
});
