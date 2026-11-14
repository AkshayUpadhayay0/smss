import type { BusStatus, MyBus, MyBusItem, MyBusScheduled } from '../../../api/student';
import { dropText, pickupText } from '../../../lib/busTimes';

// Pure view logic for the student's "Where is my bus" screen (no React Native imports: unit-tested). Ages and states come from the SERVER; the phone's
// clock is never used for a decision.

/** "just now", "40 s ago", "3 min ago", "2 h ago". */
export function ageText(seconds: number | null | undefined): string {
  if (seconds == null) return '';
  const s = Math.max(0, Math.round(seconds));
  if (s < 10) return 'just now';
  if (s < 60) return `${s} s ago`;
  if (s < 3600) return `${Math.floor(s / 60)} min ago`;
  return `${Math.floor(s / 3600)} h ago`;
}

export interface BusCard {
  /** Stable key for the list and the map marker (the plate is unique within a school). */
  key: string;
  driverName: string;
  vehicleLabel: string;
  vehiclePlate: string;
  status: BusStatus;
  statusText: string;
  /** "Updated 12 s ago" for LIVE and STALE; null for OFFLINE (the server sends no age then). */
  updatedText: string | null;
  /** A clear warning for a bus that is not LIVE; null when LIVE. */
  warning: string | null;
  hasPosition: boolean;
  /** "Approximate pickup 07:30" / "Approximate drop 14:15", each null when the school has not set it. */
  pickupText: string | null;
  dropText: string | null;
}

/** A driver of the area who is not on duty now: name, vehicle and the approximate times, nothing live. */
export interface ScheduledCard {
  key: string;
  driverName: string;
  vehicleLabel: string;
  vehiclePlate: string;
  pickupText: string | null;
  dropText: string | null;
}

export type BusScreenState =
  | { kind: 'NOT_ASSIGNED'; message: string }
  | { kind: 'NO_BUS'; areaName: string; message: string; scheduled: ScheduledCard[] }
  | { kind: 'BUSES'; areaName: string; buses: BusCard[]; scheduled: ScheduledCard[] };

const STATUS_TEXT: Record<BusStatus, string> = { LIVE: 'Live', STALE: 'Last known position', OFFLINE: 'Not sharing' };

export function toCard(b: MyBusItem): BusCard {
  const hasPosition = b.status !== 'OFFLINE' && typeof b.latitude === 'number' && typeof b.longitude === 'number';
  return {
    key: b.vehiclePlate,
    driverName: b.driverName,
    vehicleLabel: b.vehicleLabel,
    vehiclePlate: b.vehiclePlate,
    status: b.status,
    statusText: STATUS_TEXT[b.status],
    updatedText: b.status === 'OFFLINE' || b.ageSeconds == null ? null : `Updated ${ageText(b.ageSeconds)}`,
    warning:
      b.status === 'STALE'
        ? `This is the bus's last known position (${ageText(b.ageSeconds)}). It may not be updating, so do not rely on it.`
        : b.status === 'OFFLINE'
          ? 'This bus is on duty but is not sharing its location right now.'
          : null,
    hasPosition,
    pickupText: pickupText(b.morningPickupTime),
    dropText: dropText(b.afternoonDropTime),
  };
}

export function toScheduledCard(s: MyBusScheduled): ScheduledCard {
  return {
    key: s.vehiclePlate + '|' + s.driverName,
    driverName: s.driverName,
    vehicleLabel: s.vehicleLabel,
    vehiclePlate: s.vehiclePlate,
    pickupText: pickupText(s.morningPickupTime),
    dropText: dropText(s.afternoonDropTime),
  };
}

export function deriveBusState(data: MyBus): BusScreenState {
  if (!data.assigned || !data.area) {
    return { kind: 'NOT_ASSIGNED', message: data.message ?? 'You are not assigned to a school bus yet. Ask your school office.' };
  }
  const scheduled = (data.scheduled ?? []).map(toScheduledCard);
  if (data.buses.length === 0) {
    return { kind: 'NO_BUS', areaName: data.area.areaName, message: data.message ?? 'No bus is on duty for your area right now.', scheduled };
  }
  return { kind: 'BUSES', areaName: data.area.areaName, buses: data.buses.map(toCard), scheduled };
}

export interface MapPoint {
  id: string;
  name: string;
  label: string;
  status: Exclude<BusStatus, 'OFFLINE'>;
  lat: number;
  lng: number;
}

/** Only buses with a position: an OFFLINE bus never reaches the map (the server sends no coordinates for it, and a stale pin would mislead). */
export function mapPoints(buses: MyBusItem[]): MapPoint[] {
  const points: MapPoint[] = [];
  for (const b of buses) {
    if (b.status === 'OFFLINE' || typeof b.latitude !== 'number' || typeof b.longitude !== 'number') continue;
    points.push({ id: b.vehiclePlate, name: b.driverName, label: b.vehicleLabel, status: b.status, lat: b.latitude, lng: b.longitude });
  }
  return points;
}

/**
 * The JavaScript injected into the map page to move the markers. The data goes through JSON.stringify, never string concatenation, so a driver name cannot break out
 * of the call; the line separators JSON allows but JavaScript did not are escaped as well.
 */
export function buildUpdateScript(points: MapPoint[]): string {
  const json = JSON.stringify(points).split(String.fromCharCode(0x2028)).join('\\u2028').split(String.fromCharCode(0x2029)).join('\\u2029');
  return `window.__setBuses && window.__setBuses(${json}); true;`;
}
