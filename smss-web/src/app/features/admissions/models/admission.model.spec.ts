import { ACTIONS_BY_STATUS, AdmissionStatus, STATUS_META, formatDate } from './admission.model';

describe('admission model', () => {
  it('offers exactly the workflow actions the server allows for each status', () => {
    expect(ACTIONS_BY_STATUS.APPLICATION_SUBMITTED).toEqual(['start-review', 'cancel']);
    expect(ACTIONS_BY_STATUS.UNDER_REVIEW).toEqual(['approve', 'reject', 'cancel']);
    expect(ACTIONS_BY_STATUS.APPROVED).toEqual(['confirm']);
  });

  it('offers no actions once an application is finished', () => {
    for (const s of ['ADMITTED', 'REJECTED', 'CANCELLED'] as AdmissionStatus[]) expect(ACTIONS_BY_STATUS[s]).toEqual([]);
  });

  it('has a label and colour for every status', () => {
    for (const s of Object.keys(ACTIONS_BY_STATUS) as AdmissionStatus[]) {
      expect(STATUS_META[s].label).toBeTruthy();
      expect(STATUS_META[s].tone).toBeTruthy();
    }
    expect(STATUS_META.APPLICATION_SUBMITTED.tone).toBe('gray');
    expect(STATUS_META.UNDER_REVIEW.tone).toBe('blue');
    expect(STATUS_META.APPROVED.tone).toBe('green');
    expect(STATUS_META.REJECTED.tone).toBe('red');
    expect(STATUS_META.ADMITTED.tone).toBe('violet');
    expect(STATUS_META.CANCELLED.tone).toBe('gray-strike');
  });

  it('formats ISO dates and tolerates blanks', () => {
    expect(formatDate('2026-10-12T00:00:00')).toBe('12 Oct 2026');
    expect(formatDate(null)).toBe('');
  });
});
