import { deriveDutyView, formatClock, lastSentText, startBlockedMessage } from '../src/features/driver/dutyView';
import type { PermSnapshot } from '../src/features/driver/permissions';

const ALL: PermSnapshot = { foreground: 'granted', background: 'granted', notifications: 'granted' };

describe('deriveDutyView', () => {
  it('server on duty + service running = sharing', () => expect(deriveDutyView({ serverOnDuty: true, serviceRunning: true, stopped: null })).toEqual({ kind: 'SHARING' }));
  it('server on duty + service NOT running = offer Resume (also when a stop reason is remembered)', () => {
    expect(deriveDutyView({ serverOnDuty: true, serviceRunning: false, stopped: null })).toEqual({ kind: 'NOT_SHARING' });
    expect(deriveDutyView({ serverOnDuty: true, serviceRunning: false, stopped: 'UNKNOWN_TOKEN' })).toEqual({ kind: 'NOT_SHARING' });
  });
  it('server off duty + a remembered stop = a clear "duty ended" state with the reason', () => {
    expect(deriveDutyView({ serverOnDuty: false, serviceRunning: false, stopped: 'DRIVER_DEACTIVATED' })).toEqual({ kind: 'ENDED', reason: 'DRIVER_DEACTIVATED' });
  });
  it('server off duty and nothing remembered = off duty', () => expect(deriveDutyView({ serverOnDuty: false, serviceRunning: false, stopped: null })).toEqual({ kind: 'OFF' }));
});

describe('startBlockedMessage (why "Mark today\'s attendance" is disabled)', () => {
  const me = (canStartDuty: boolean, startBlockedReason: string | null = null) => ({ canStartDuty, startBlockedReason });
  it('says the server\'s reason when there is no vehicle', () => {
    expect(startBlockedMessage(me(false, 'No vehicle is assigned to you. Ask your school to assign one.'), ALL, 35)).toContain('No vehicle is assigned');
  });
  it('asks for permissions when any is missing', () => {
    expect(startBlockedMessage(me(true), { ...ALL, background: 'denied' }, 35)).toContain('Allow location access');
    expect(startBlockedMessage(me(true), null, 35)).toContain('Allow location access');
  });
  it('is null (enabled) when the server allows it and everything is granted', () => expect(startBlockedMessage(me(true), ALL, 35)).toBeNull());
  it('says it is loading before the profile arrives', () => expect(startBlockedMessage(null, ALL, 35)).toMatch(/Loading/));
});

describe('display helpers use the server\'s numbers', () => {
  it('lastSentText', () => {
    expect(lastSentText(null)).toBe('Nothing sent yet');
    expect(lastSentText(4)).toBe('Last sent just now');
    expect(lastSentText(40)).toBe('Last sent 40 s ago');
    expect(lastSentText(185)).toBe('Last sent 3 min ago');
    expect(lastSentText(7300)).toBe('Last sent 2 h ago');
  });
  it('formatClock gives HH:MM and tolerates junk', () => {
    expect(formatClock('2026-11-13T10:05:00Z')).toMatch(/^\d{2}:\d{2}$/);
    expect(formatClock(null)).toBe('');
    expect(formatClock('nonsense')).toBe('');
  });
});
