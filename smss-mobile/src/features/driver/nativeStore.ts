import * as SecureStore from 'expo-secure-store';
import { createDutyStore, type KeyValue } from './dutyStore';

// Duty state lives in the OS keystore like the login does (the tracking token is a secret). Both the UI and the background task use this one instance.
const secureKv: KeyValue = {
  get: (key) => SecureStore.getItemAsync(key),
  set: (key, value) => SecureStore.setItemAsync(key, value),
  del: (key) => SecureStore.deleteItemAsync(key),
};

export const dutyStore = createDutyStore(secureKv);
