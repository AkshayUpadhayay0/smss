import { Tabs } from 'expo-router';
import { tabIcon, tabScreenOptions } from '../../../src/components/TabBar';

export default function TabsLayout() {
  return (
    <Tabs screenOptions={tabScreenOptions}>
      <Tabs.Screen name="index" options={{ title: 'Home', tabBarIcon: tabIcon('home') }} />
      <Tabs.Screen name="attendance" options={{ title: 'Attendance', tabBarIcon: tabIcon('check-square') }} />
      <Tabs.Screen name="homework" options={{ title: 'Homework', tabBarIcon: tabIcon('book-open') }} />
      <Tabs.Screen name="marks" options={{ title: 'Marks', tabBarIcon: tabIcon('award') }} />
      <Tabs.Screen name="leave" options={{ title: 'Leave', tabBarIcon: tabIcon('calendar') }} />
    </Tabs>
  );
}
