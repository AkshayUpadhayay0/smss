import { resolveRole } from '../src/auth/roles';
import { cardChip, chipLabel, classLabel, lastThirtyDays, monthWindow, passwordProblem, readOnlyBanner, rupees, unreadBadge } from '../src/features/family/rules';
import { FORGOT_PATH, webPageUrl } from '../src/lib/webLinks';

const mockRequest = jest.fn().mockResolvedValue({ count: 3 });
jest.mock('../src/api/index', () => ({ api: { request: (...a: unknown[]) => mockRequest(...a) } }));

import * as family from '../src/api/family';

describe('family api module', () => {
  beforeEach(() => mockRequest.mockClear());

  it('only ever reads /api/Family and never passes a method (GET) or a body', async () => {
    await family.getChildren();
    await family.getProfile('S 1');
    await family.getAttendance('S1', '2026-11-01', '2026-11-30');
    await family.getHomework('S1');
    await family.getTimetable('S1');
    await family.getExamSchedule('S1');
    await family.getReportCards('S1');
    await family.getFees('S1');
    await family.getReceipt('S1', 9);
    await family.getLeave('S1');
    await family.getNotices();
    await family.getUnreadNotices();
    await family.getNotice(4);
    await family.getCalendar('2026-11-01', '2026-11-30');
    expect(mockRequest.mock.calls.length).toBe(14);
    for (const [path, opts] of mockRequest.mock.calls) {
      expect(path).toMatch(/^\/api\/Family\//);
      expect(opts?.method).toBeUndefined();
      expect(opts?.body).toBeUndefined();
    }
  });

  it('encodes the child id in the path and builds the report-card pdf path under the child', () => {
    expect(family.reportCardPdfPath('A/B', 5)).toBe('/api/Family/children/A%2FB/report-cards/5/pdf');
  });

  it('unread count returns the number', async () => {
    await expect(family.getUnreadNotices()).resolves.toBe(3);
  });
});

describe('guardian role', () => {
  it('maps Guardian to GUARDIAN and never to a staff or student app', () => {
    expect(resolveRole(['Guardian'])).toBe('GUARDIAN');
    expect(resolveRole(['Student', 'Guardian'])).toBe('STUDENT');
  });
});

describe('family rules', () => {
  it('words the chips and the read-only banner', () => {
    expect(chipLabel('PASSED_OUT')).toBe('Passed out');
    expect(chipLabel('LEFT')).toBe('Left the school');
    expect(chipLabel('TRANSFERRED_OUT')).toBe('Transferred out');
    expect(readOnlyBanner(null)).toBeNull();
    expect(readOnlyBanner('LEFT')).toContain('read-only');
    expect(readOnlyBanner('PENDING_PROMOTION')).toContain('not been moved');
  });
  it('labels class and card chip', () => {
    expect(classLabel({ className: 'Class 5', sectionName: 'A' })).toBe('Class 5 – A');
    expect(classLabel({ className: null, sectionName: null })).toBe('Not in a class');
    const base = { studentId: 's', fullName: 'n', admissionNumber: '1', statusName: 'Active', academicYearName: null, className: null, sectionName: null, rollNumber: null, readOnly: false, feeDueNow: 0 };
    expect(cardChip({ ...base, chipState: null, attendanceToday: 'Absent' }).tone).toBe('danger');
    expect(cardChip({ ...base, chipState: null, attendanceToday: null }).label).toBe('Today: not marked yet');
    expect(cardChip({ ...base, chipState: 'LEFT', attendanceToday: 'Present' }).label).toBe('Left the school');
  });
  it('formats money, badge and windows', () => {
    expect(rupees(1500)).toBe('₹1,500');
    expect(unreadBadge(0)).toBeUndefined();
    expect(unreadBadge(undefined)).toBeUndefined();
    expect(unreadBadge(7)).toBe('7');
    expect(unreadBadge(150)).toBe('99+');
    expect(lastThirtyDays(new Date(2026, 10, 26))).toEqual({ from: '2026-10-28', to: '2026-11-26' });
    expect(monthWindow(2026, 2)).toEqual({ from: '2026-02-01', to: '2026-02-28' });
  });
  it('checks the change-password form', () => {
    expect(passwordProblem('', 'abcdefgh', 'abcdefgh')).toContain('current');
    expect(passwordProblem('old', 'short', 'short')).toContain('at least 8');
    expect(passwordProblem('oldpassword', 'oldpassword', 'oldpassword')).toContain('different');
    expect(passwordProblem('oldpassword', 'newpassword', 'other')).toContain('do not match');
    expect(passwordProblem('oldpassword', 'newpassword', 'newpassword')).toBeNull();
  });
});

describe('web links', () => {
  it('uses the https origin of the privacy policy address', () => {
    expect(webPageUrl('https://smss-erp.digital/privacy-policy', FORGOT_PATH)).toBe('https://smss-erp.digital/parent/forgot');
  });
  it('is hidden when not configured, not https, or not a url', () => {
    expect(webPageUrl(null, FORGOT_PATH)).toBeNull();
    expect(webPageUrl('http://x.test/p', FORGOT_PATH)).toBeNull();
    expect(webPageUrl('not a url', FORGOT_PATH)).toBeNull();
  });
});
