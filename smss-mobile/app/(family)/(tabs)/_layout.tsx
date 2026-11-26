import { Tabs } from 'expo-router';
import { tabIcon, tabScreenOptions } from '../../../src/components/TabBar';
import { useUnread } from '../../../src/features/family/queries';
import { unreadBadge } from '../../../src/features/family/rules';

export default function TabsLayout() {
  const unread = useUnread();
  return (
    <Tabs screenOptions={tabScreenOptions}>
      <Tabs.Screen name="index" options={{ title: 'Family', tabBarIcon: tabIcon('users') }} />
      <Tabs.Screen name="notices" options={{ title: 'Notices', tabBarIcon: tabIcon('bell'), tabBarBadge: unreadBadge(unread.data) }} />
      <Tabs.Screen name="calendar" options={{ title: 'Calendar', tabBarIcon: tabIcon('calendar') }} />
      <Tabs.Screen name="account" options={{ title: 'Account', tabBarIcon: tabIcon('user') }} />
    </Tabs>
  );
}
