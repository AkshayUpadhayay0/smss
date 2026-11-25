import { ACTIONS_BY_STATUS, AdmissionStatus, STATUS_META, formatDate } from './admission.model';

describe('admission model', () => {
  it('offers exactly the workflow actions the server allows for each status', () => {
    expect(ACTIONS_BY_STATUS.APPLICATION_SUBMITTED).toEqual(['confirm', 'reject', 'cancel']);   // two steps: added, then confirmed
  });

  it('offers no actions once an application is finished', () => {
    for (const s of ['ADMITTED', 'REJECTED', 'CANCELLED'] as AdmissionStatus[]) expect(ACTIONS_BY_STATUS[s]).toEqual([]);
  });

  it('has a label and colour for every status', () => {
    for (const s of Object.keys(ACTIONS_BY_STATUS) as AdmissionStatus[]) {
      expect(STATUS_META[s].label).toBeTruthy();
      expect(STATUS_META[s].tone).toBeTruthy();
    }
    expect(STATUS_META.APPLICATION_SUBMITTED.tone).toBe('blue');
    expect(STATUS_META.REJECTED.tone).toBe('red');
    expect(STATUS_META.ADMITTED.tone).toBe('violet');
    expect(STATUS_META.CANCELLED.tone).toBe('gray-strike');
  });

  it('the words people see are Application added and Confirmed; the codes stay', () => {
    expect(STATUS_META.APPLICATION_SUBMITTED.label).toBe('Application added');
    expect(STATUS_META.ADMITTED.label).toBe('Confirmed');
    expect(Object.keys(STATUS_META).sort()).toEqual(['ADMITTED', 'APPLICATION_SUBMITTED', 'CANCELLED', 'REJECTED']);
  });

  it('formats ISO dates and tolerates blanks', () => {
    expect(formatDate('2026-10-12T00:00:00')).toBe('12 Oct 2026');
    expect(formatDate(null)).toBe('');
  });
});
