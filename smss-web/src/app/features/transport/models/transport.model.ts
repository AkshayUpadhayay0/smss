// Mirrors the Transport DTOs of the API (TransportAreaDto, VehicleDto, DriverDto ...). Dates are 'yyyy-MM-dd' strings.

/** Only the School Admin configures transport; the Principal reads. The API re-checks every write (this only decides whether the buttons show). */
export const TRANSPORT_MANAGER_ROLES = ['School Admin'] as const;

export type ExpiryStatus = 'EXPIRED' | 'EXPIRING' | 'OK' | 'NOT_SET';

export interface LookupItem {
  id: number;
  name: string;
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

// ------------------------------------------------------------------ areas
export interface TransportArea {
  areaId: number;
  areaName: string;
  distanceKm: number | null;
  transportFee: number;
  remarks: string | null;
  statusName: string;
  activeDriverCount: number;
}

export interface TransportAreaRequest {
  areaName: string;
  distanceKm: number | null;
  transportFee: number;
  remarks: string | null;
}

// ---------------------------------------------------------------- vehicles
export interface Vehicle {
  vehicleId: number;
  vehicleLabel: string;
  registrationNumber: string;
  vehicleTypeId: number;
  vehicleTypeName: string;
  ownershipId: number;
  ownershipName: string;
  isRented: boolean;
  modelYear: number | null;
  seatingCapacity: number | null;
  ownerName: string | null;
  ownerContact: string | null;
  lastServiceDate: string | null;
  pucExpiryDate: string | null;
  pucStatus: ExpiryStatus;
  pucDaysLeft: number | null;
  insuranceExpiryDate: string | null;
  insuranceStatus: ExpiryStatus;
  insuranceDaysLeft: number | null;
  statusName: string;
  activeDriverCount: number;
}

export interface VehicleRequest {
  vehicleLabel: string;
  registrationNumber: string;
  vehicleTypeId: number;
  ownershipId: number;
  modelYear: number | null;
  seatingCapacity: number | null;
  ownerName: string | null;
  ownerContact: string | null;
  lastServiceDate: string | null;
  pucExpiryDate: string | null;
  insuranceExpiryDate: string | null;
}

export interface VehicleOwnership extends LookupItem {
  /** A rented / contract vehicle: the owner / vendor fields apply. */
  isRented: boolean;
}

export interface VehicleLookups {
  vehicleTypes: LookupItem[];
  ownerships: VehicleOwnership[];
}

// ----------------------------------------------------------------- drivers
export interface DriverArea {
  areaId: number;
  areaName: string;
  isActive: boolean;
  /** "HH:mm" or null: the approximate time the bus reaches this area in the morning (school local time). */
  morningPickupTime: string | null;
  /** "HH:mm" or null: the approximate time the bus reaches this area in the afternoon. */
  afternoonDropTime: string | null;
}

export interface DriverListItem {
  driverId: string;
  fullName: string;
  mobileNumber: string;
  dlNumber: string;
  dlValidUpto: string;
  dlStatus: ExpiryStatus;
  dlDaysLeft: number | null;
  vehicleId: number | null;
  vehicleLabel: string | null;
  vehiclePlate: string | null;
  areaNames: string[];
  statusName: string;
  photoUrl: string | null;
  hasLogin: boolean;
}

export interface Driver {
  driverId: string;
  fullName: string;
  mobileNumber: string;
  email: string | null;
  address: string;
  dlNumber: string;
  dlValidUpto: string;
  dlStatus: ExpiryStatus;
  dlDaysLeft: number | null;
  vehicleId: number | null;
  vehicleLabel: string | null;
  vehiclePlate: string | null;
  areas: DriverArea[];
  statusName: string;
  photoUrl: string | null;
  hasLogin: boolean;
  loginId: string | null;
}

export interface DriverRequest {
  fullName: string;
  mobileNumber: string;
  email: string | null;
  address: string;
  dlNumber: string;
  dlValidUpto: string;
  vehicleId: number | null;
  /** The driver's areas, each with optional approximate times. The list REPLACES the previous set (an area not listed is removed; a null time clears it). */
  areas: DriverAreaRequest[];
}

export interface DriverAreaRequest {
  areaId: number;
  morningPickupTime: string | null;
  afternoonDropTime: string | null;
}

export interface DriverLookups {
  vehicles: LookupItem[];
  areas: LookupItem[];
}

export interface DriverPhoto {
  driverId: string;
  photoUrl: string | null;
}

export interface DriverLoginCreated {
  driverId: string;
  displayName: string;
  orgUserId: string;
  temporaryPassword: string;
  role: string;
  emailSent: boolean;
  emailSentTo: string | null;
}

// ----------------------------------------------------------------- display
export const STATUS_FILTERS = [
  { label: 'All', value: '' },
  { label: 'Active', value: 'Active' },
  { label: 'Inactive', value: 'Inactive' },
];

/** Cell text for an expiry badge, e.g. "Expired", "Expiring in 12 days", "OK", "Not set". */
export function expiryLabel(status: ExpiryStatus | string, daysLeft: number | null): string {
  switch (status) {
    case 'EXPIRED':
      return 'Expired';
    case 'EXPIRING':
      return daysLeft === 0 ? 'Expires today' : `Expiring in ${daysLeft ?? '?'} ${daysLeft === 1 ? 'day' : 'days'}`;
    case 'OK':
      return 'OK';
    default:
      return 'Not set';
  }
}

/** Pill colour of an expiry badge cell (the cell text comes from expiryLabel). */
export function expiryTone(label: unknown): string {
  const t = String(label ?? '');
  if (t === 'Expired') return 'red';
  if (t.startsWith('Expir')) return 'yellow';
  if (t === 'OK') return 'green';
  return 'gray';
}

/** 'yyyy-MM-dd' -> '31 Jan 2027' (date only, no time zone shifting). */
export function formatDate(value: string | null | undefined): string {
  if (!value) return '';
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
  if (!m) return value;
  const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  return `${Number(m[3])} ${months[Number(m[2]) - 1]} ${m[1]}`;
}

export function formatMoney(value: number | null | undefined): string {
  return value == null ? '' : value.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

/** Same as the server: whitespace removed, upper case (so "up32 ab 1234" is accepted as UP32AB1234). */
export function normalizeCode(value: unknown): string {
  return String(value ?? '').replace(/\s+/g, '').toUpperCase();
}

// ----------------------------------------------------------------- live map (Stage 2)
export type DutyStatus = 'LIVE' | 'STALE' | 'OFFLINE';

export interface LiveDriver {
  driverId: string;
  driverName: string;
  vehicleId: number;
  vehicleLabel: string;
  vehiclePlate: string;
  areas: string[];
  hasFix: boolean;
  latitude: number | null;
  longitude: number | null;
  accuracyMeters: number | null;
  speedKmh: number | null;
  headingDegrees: number | null;
  status: DutyStatus;
  updatedAt: string | null;
  /** Age of the last fix in seconds AT THE TIME OF THE RESPONSE (server clock). */
  ageSeconds: number | null;
  sessionStartedAt: string;
}

export interface LiveBoard {
  serverTime: string;
  counts: { live: number; stale: number; offline: number };
  items: LiveDriver[];
}

/** "just now", "40 s ago", "3 min ago", "2 h ago". */
export function formatAge(seconds: number | null | undefined): string {
  if (seconds == null) return 'no location yet';
  const s = Math.max(0, Math.round(seconds));
  if (s < 10) return 'just now';
  if (s < 60) return `${s} s ago`;
  if (s < 3600) return `${Math.floor(s / 60)} min ago`;
  return `${Math.floor(s / 3600)} h ago`;
}

/** Text for the pill on the map and the label in the list: driver and vehicle. */
export function liveLabel(d: Pick<LiveDriver, 'driverName' | 'vehicleLabel'>): string {
  return `${d.driverName} · ${d.vehicleLabel}`;
}

/** Escapes text that goes into a Leaflet divIcon (HTML). Driver and vehicle names are typed by users. */
export function escapeHtml(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

// ----------------------------------------------------------- student transport (Stage 3)
/** Who may change a student's transport area: the same role as every other Transport write. The Principal reads. */
export interface StudentTransportAssignment {
  areaId: number;
  areaName: string;
  /** False when the area was deactivated after the student was assigned: the school should move the student. */
  areaIsActive: boolean;
}

export interface StudentTransport {
  studentId: string;
  studentName: string;
  academicYearId: number;
  academicYearName: string;
  isCurrentYear: boolean;
  assignment: StudentTransportAssignment | null;
}
