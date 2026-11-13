import { api } from '../../api';
import type { DriverMe } from './dutyView';

// Endpoints used with the driver's LOGIN (JWT). The school and the driver always come from the token: nothing here sends an id.
// The location pings are NOT here: they use the tracking token (dutyHttp.ts).

export interface DutyStarted {
  sessionId: number;
  startedAt: string;
  /** true when an open, fresh session was resumed: the server issued a NEW token and the old one no longer works. */
  resumed: boolean;
  vehicle: { vehicleId: number; vehicleLabel: string; vehiclePlate: string; isActive: boolean };
  /** Shown once. Goes straight into secure storage and nowhere else. */
  trackingToken: string;
  expiresAt: string;
  intervalSeconds: number;
  minIntervalSeconds: number;
}

export const getMe = () => api.request<DriverMe>('/api/Drivers/me');
export const startDutyRequest = () => api.request<DutyStarted>('/api/DriverDuty/start', { method: 'POST' });
/** Ends duty with the login. Idempotent on the server. */
export const endDutyRequest = () => api.request<{ ended: boolean }>('/api/DriverDuty/end', { method: 'POST' });
