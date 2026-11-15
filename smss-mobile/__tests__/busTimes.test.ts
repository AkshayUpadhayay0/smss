import type { MyBus, MyBusItem, MyBusScheduled } from '../src/api/student';
import { deriveBusState, toCard, toScheduledCard } from '../src/features/student/bus/busView';
import { cleanTime, dropText, homeTimeLine, pickupText, timesSummary } from '../src/lib/busTimes';

const bus = (over: Partial<MyBusItem> = {}): MyBusItem => ({
  driverName: 'Ramesh Kumar', vehicleLabel: 'Bus 1', vehiclePlate: 'UP32AB1234', status: 'LIVE', latitude: 26.85, longitude: 80.95, ageSeconds: 12,
  morningPickupTime: null, afternoonDropTime: null, ...over,
});
const sched = (over: Partial<MyBusScheduled> = {}): MyBusScheduled => ({
  driverName: 'Suresh Yadav', vehicleLabel: 'Van 2', vehiclePlate: 'UP32CD2222', morningPickupTime: null, afternoonDropTime: null, ...over,
});
const data = (over: Partial<MyBus> = {}): MyBus => ({
  assigned: true, area: { areaName: 'Civil Lines' }, buses: [], scheduled: [], serverTime: '2026-11-14T10:00:00Z', message: null, ...over,
});

describe('time wording and cleaning', () => {
  it('uses the approved words: Approximate pickup / Approximate drop', () => {
    expect(pickupText('07:30')).toBe('Approximate pickup 07:30');
    expect(dropText('14:15')).toBe('Approximate drop 14:15');
    expect(timesSummary({ morningPickupTime: '07:30', afternoonDropTime: '14:15' })).toBe('Approximate pickup 07:30 · Approximate drop 14:15');
  });
  it('shows either half alone and nothing when there is no time', () => {
    expect(timesSummary({ morningPickupTime: '07:30', afternoonDropTime: null })).toBe('Approximate pickup 07:30');
    expect(timesSummary({ morningPickupTime: null, afternoonDropTime: '14:15' })).toBe('Approximate drop 14:15');
    expect(timesSummary({ morningPickupTime: null, afternoonDropTime: null })).toBeNull();
    expect(timesSummary({})).toBeNull();
  });
  it('ignores anything that is not HH:mm instead of showing it', () => {
    for (const bad of [null, undefined, '', '7:30', '24:00', '07:30:00', 'soon', '<b>07:30</b>']) expect(cleanTime(bad)).toBeNull();
    expect(cleanTime(' 07:30 ')).toBe('07:30');
    expect(pickupText('soon')).toBeNull();
  });
});

describe('the Home card line', () => {
  it('is the earliest pickup across the area (buses on duty and scheduled)', () => {
    expect(homeTimeLine([{ morningPickupTime: '07:30', afternoonDropTime: '14:15' }, { morningPickupTime: '06:45', afternoonDropTime: null }])).toBe('Pickup about 06:45');
  });
  it('falls back to the earliest drop when no pickup time is set anywhere', () => {
    expect(homeTimeLine([{ morningPickupTime: null, afternoonDropTime: '15:00' }, { morningPickupTime: null, afternoonDropTime: '14:15' }])).toBe('Drop about 14:15');
  });
  it('is null (the card keeps its normal text) when there is no time, no entry, or only malformed values', () => {
    expect(homeTimeLine([])).toBeNull();
    expect(homeTimeLine([{ morningPickupTime: null, afternoonDropTime: null }])).toBeNull();
    expect(homeTimeLine([{ morningPickupTime: 'x', afternoonDropTime: '7pm' }])).toBeNull();
  });
});

describe('cards carry the times', () => {
  it('a bus on duty shows its own pickup and drop lines', () => {
    const c = toCard(bus({ morningPickupTime: '07:30', afternoonDropTime: '14:15' }));
    expect([c.pickupText, c.dropText]).toEqual(['Approximate pickup 07:30', 'Approximate drop 14:15']);
  });
  it('a bus without times has no time lines', () => {
    const c = toCard(bus());
    expect([c.pickupText, c.dropText]).toEqual([null, null]);
  });
  it('a scheduled driver has name, vehicle and times and nothing live', () => {
    const c = toScheduledCard(sched({ morningPickupTime: '08:00', afternoonDropTime: null }));
    expect(c).toEqual({ key: 'UP32CD2222|Suresh Yadav', driverName: 'Suresh Yadav', vehicleLabel: 'Van 2', vehiclePlate: 'UP32CD2222', pickupText: 'Approximate pickup 08:00', dropText: null });
    expect(Object.keys(c).some((k) => /^(status|latitude|longitude|ageSeconds|position)$/i.test(k))).toBe(false);
  });
});

describe('screen states with a schedule', () => {
  it('no bus on duty but a schedule: the schedule is still shown', () => {
    const s = deriveBusState(data({ message: 'No bus is on duty for your area right now.', scheduled: [sched({ morningPickupTime: '07:30' })] }));
    expect(s.kind).toBe('NO_BUS');
    if (s.kind === 'NO_BUS') {
      expect(s.scheduled.map((x) => x.driverName)).toEqual(['Suresh Yadav']);
      expect(s.message).toContain('No bus is on duty');
    }
  });
  it('a bus on duty plus a scheduled driver: both lists, in their own sections', () => {
    const s = deriveBusState(data({ buses: [bus({ morningPickupTime: '07:30' })], scheduled: [sched()] }));
    expect(s.kind).toBe('BUSES');
    if (s.kind === 'BUSES') {
      expect(s.buses).toHaveLength(1);
      expect(s.scheduled).toHaveLength(1);
    }
  });
  it('an older server answer without scheduled[] still works', () => {
    const old = { ...data({ buses: [] }) } as Partial<MyBus>;
    delete old.scheduled;
    const s = deriveBusState(old as MyBus);
    expect(s).toMatchObject({ kind: 'NO_BUS', scheduled: [] });
  });
  it('not assigned has no schedule', () => {
    expect(deriveBusState(data({ assigned: false, area: null })).kind).toBe('NOT_ASSIGNED');
  });
});
