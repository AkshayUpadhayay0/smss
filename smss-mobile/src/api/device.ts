import { api } from './index';

export type DevicePlatform = 'ANDROID' | 'IOS';

export interface RegisterDeviceBody {
  fcmToken: string;
  platform: DevicePlatform;
  deviceLabel?: string;
  appVersion?: string;
}

// POST /api/Device/register. The school and user come from the access token, never from the body.
export async function registerDevice(body: RegisterDeviceBody): Promise<void> {
  await api.request<unknown>('/api/Device/register', { method: 'POST', body });
}

// POST /api/Device/unregister {fcmToken}. Called before the session ends, while the access token is still valid.
export async function unregisterDevice(fcmToken: string): Promise<void> {
  await api.request<unknown>('/api/Device/unregister', { method: 'POST', body: { fcmToken } });
}
