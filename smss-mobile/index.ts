// App entry. The duty tracking task MUST be defined here, at module scope, before the router loads: when Android restarts the app only to deliver a
// background location update there is no UI, and expo-task-manager can only run a task that is already defined.
import './src/features/driver/trackingTask';
import 'expo-router/entry';
