import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { SectionFeeStatus } from '../../models/section-fee-status.model';
import { SectionFeeStatusComponent } from './section-fee-status.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const MY = { today: '2026-11-05', academicYearId: 40, academicYearName: '2026-27', sections: [{ classId: 34, className: 'Nursery', sectionId: 38, sectionName: 'A', rosterCount: 4, markedToday: false }, { classId: 35, className: 'LKG', sectionId: 39, sectionName: 'A', rosterCount: 3, markedToday: false }] };
const DATA: SectionFeeStatus = {
  sectionId: 38, classId: 34, className: 'Nursery', sectionName: 'A', academicYearId: 40, academicYearName: '2026-27',
  summary: { students: 4, fullyPaid: 1, partiallyPaid: 1, unpaid: 0, overdue: 2, notSet: 0 },
  students: [
    { rollNumber: '1', fullName: 'Aanya Thapa', status: 'Paid', totalDue: 48700, totalPaid: 48700, balance: 0 },
    { rollNumber: '2', fullName: 'Harsh Dobhal', status: 'Overdue', totalDue: 48700, totalPaid: 11600, balance: 37100 },
    { rollNumber: '3', fullName: 'Pari Rana', status: 'Partially Paid', totalDue: 48700, totalPaid: 20000, balance: 28700 },
    { rollNumber: '4', fullName: 'Rohan, "Ro" Bhatt', status: 'Overdue', totalDue: 48700, totalPaid: 0, balance: 48700 },
  ],
} as SectionFeeStatus;

describe('SectionFeeStatusComponent', () => {
  let fixture: ComponentFixture<SectionFeeStatusComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const names = () => [...el().querySelectorAll('tbody td.name')].map((c) => c.textContent?.trim());

  function open(sections: unknown = MY, status: SectionFeeStatus | null = DATA) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: ['Teacher'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SectionFeeStatusComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Attendance/my-sections`).flush(env(sections));
    fixture.detectChanges();
    if (status) {
      http.expectOne((r) => r.url === `${API}/api/Fee/section-status`).flush(env(status));
      fixture.detectChanges();
    }
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('asks for the first section and shows the summary counts', () => {
    open();
    expect(cmp()['sectionControl'].value).toBe('38');
    const tiles = [...el().querySelectorAll('.tile')].map((t) => (t.textContent ?? '').replace(/\s+/g, ' ').trim());
    expect(tiles).toEqual(['4Students', '1Fully paid', '1Partially paid', '0Unpaid', '2Overdue']);
  });

  it('lists students highest balance first', () => {
    open();
    expect(names()).toEqual(['Rohan, "Ro" Bhatt', 'Harsh Dobhal', 'Pari Rana', 'Aanya Thapa']);
  });

  it('filters by status and by name / roll number', () => {
    open();
    cmp()['statusControl'].setValue('Overdue');
    fixture.detectChanges();
    expect(names()).toEqual(['Rohan, "Ro" Bhatt', 'Harsh Dobhal']);
    cmp()['statusControl'].setValue('All');
    const input = el().querySelector('input[type=search]') as HTMLInputElement;
    input.value = 'pari'; input.dispatchEvent(new Event('input')); fixture.detectChanges();
    expect(names()).toEqual(['Pari Rana']);
    input.value = '1'; input.dispatchEvent(new Event('input')); fixture.detectChanges();
    expect(names()).toEqual(['Aanya Thapa']);
    input.value = 'zzz'; input.dispatchEvent(new Event('input')); fixture.detectChanges();
    expect(text()).toContain('No student matches the filter.');
  });

  it('is read-only: no receipts, modes, concessions, pay buttons or inputs beyond the filters', () => {
    open();
    expect(text()).not.toMatch(/receipt|mode|concession|discount|collect|pay now|void/i);
    expect(el().querySelectorAll('tbody input, tbody button').length).toBe(0);
  });

  it('changing the section asks for that section', () => {
    open();
    cmp()['sectionControl'].setValue('39');
    const r = http.expectOne((x) => x.url === `${API}/api/Fee/section-status`);
    expect(r.request.params.get('sectionId')).toBe('39');
    r.flush(env({ ...DATA, sectionId: 39, className: 'LKG' }));
  });

  it('a teacher who is not a class teacher sees a clear empty state, not an error', () => {
    open({ ...MY, sections: [] }, null);
    expect(text()).toContain('You are not the class teacher of any section');
    expect(el().querySelector('[role="alert"]')).toBeNull();
  });

  it('a failed load shows one calm message with a retry', () => {
    open(MY, null);
    http.expectOne((r) => r.url === `${API}/api/Fee/section-status`).flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(el().querySelector('[role="alert"]')?.textContent).toContain('could not be loaded');
  });

  it('CSV export writes what is on screen (filter and order), one file', () => {
    open();
    const names: string[] = [];
    let csv = '';
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) { names.push(this.download); });
    vi.spyOn(URL, 'createObjectURL').mockImplementation((b: Blob | MediaSource) => { void (b as Blob).text().then((t) => (csv = t)); return 'blob:x'; });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    cmp()['statusControl'].setValue('Overdue');
    cmp()['exportCsv']();
    expect(names).toEqual(['fee-status-Nursery-A.csv']);
    return new Promise<void>((res) => setTimeout(() => {
      const lines = csv.replace('\uFEFF', '').trimEnd().split('\r\n');
      expect(lines[0]).toBe('Roll,Student,Status,Total due,Paid,Balance');
      expect(lines[1]).toBe('4,"Rohan, ""Ro"" Bhatt",Overdue,48700,0,48700');
      expect(lines).toHaveLength(3);
      res();
    }, 10));
  });
});
