import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { AcademicYear, AcademicYearUsage } from '../../models/academic-year.model';
import { AcademicYearListComponent, usageSummary } from './academic-year-list.component';

const API = environment.apiUrl;
const NONE: AcademicYearUsage = { enrolments: 0, feeStructures: 0, attendanceRows: 0, timetableEntries: 0, classTeachers: 0, homework: 0, tests: 0, events: 0 };

function year(over: Partial<AcademicYear>): AcademicYear {
  return {
    academicYearId: 1, schoolId: 'S', yearName: '2026-27', startDate: '2026-04-01', endDate: '2027-03-31', isCurrent: false, statusId: 1, statusName: 'Active',
    createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z', state: 'IN_SESSION', isClosed: false, usage: NONE, ...over,
  };
}
const envelope = (data: unknown) => ({ status: true, statusCode: 200, message: 'ok', data });

describe('usageSummary', () => {
  it('is an em dash when nothing depends on the year', () => expect(usageSummary(NONE)).toBe('—'));
  it('is an em dash when the server sent no usage', () => expect(usageSummary(undefined)).toBe('—'));
  it('lists only what is there, with singular and plural', () =>
    expect(usageSummary({ ...NONE, enrolments: 12, feeStructures: 1, events: 2 })).toBe('12 enrolments, 1 fee structure, 2 events'));
});

describe('AcademicYearListComponent (state, close and reopen)', () => {
  let fixture: ComponentFixture<AcademicYearListComponent>;
  let http: HttpTestingController;
  let confirmAnswer = true;
  const root = () => fixture.nativeElement as HTMLElement;
  const rows = () => Array.from(root().querySelectorAll('tbody tr'));
  const rowOf = (name: string) => rows().find((r) => r.textContent?.includes(name))!;
  const buttonLabels = (r: Element) => Array.from(r.querySelectorAll('app-button')).map((b) => b.textContent?.trim());
  const clickButton = (r: Element, label: string) =>
    (Array.from(r.querySelectorAll('app-button')).find((b) => b.textContent?.trim() === label)!.querySelector('button') as HTMLButtonElement).click();

  function open(years: AcademicYear[]) {
    confirmAnswer = true;
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: ConfirmDialogService, useValue: { confirm: async () => confirmAnswer } }],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AcademicYearListComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/AcademicYear`).flush(envelope(years));
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  const current = year({ academicYearId: 1, yearName: 'CURR-YEAR', isCurrent: true, state: 'CURRENT', usage: { ...NONE, enrolments: 5 } });
  const past = year({ academicYearId: 2, yearName: 'PAST-YEAR', startDate: '2025-04-01', endDate: '2026-03-31', state: 'PAST' });
  const closed = year({ academicYearId: 3, yearName: 'SHUT-YEAR', startDate: '2024-04-01', endDate: '2025-03-31', state: 'CLOSED', isClosed: true });

  it('shows the derived state as a label and what depends on the year', () => {
    open([current, past, closed]);
    expect(rowOf('CURR-YEAR').textContent).toContain('Current');
    expect(rowOf('CURR-YEAR').textContent).toContain('5 enrolments');
    expect(rowOf('PAST-YEAR').textContent).toContain('Past');
    expect(rowOf('SHUT-YEAR').textContent).toContain('Closed');
  });

  it('offers Close only on a year that is neither current nor closed, and Reopen only on a closed one', () => {
    open([current, past, closed]);
    expect(buttonLabels(rowOf('CURR-YEAR'))).not.toContain('Close');
    expect(buttonLabels(rowOf('CURR-YEAR'))).not.toContain('Reopen');
    expect(buttonLabels(rowOf('PAST-YEAR'))).toContain('Close');
    expect(buttonLabels(rowOf('SHUT-YEAR'))).toContain('Reopen');
    expect(buttonLabels(rowOf('SHUT-YEAR'))).not.toContain('Close');
    expect(buttonLabels(rowOf('SHUT-YEAR'))).not.toContain('Set as Current');   // a closed year can't become current
  });

  it('Close asks first, then posts to /close and shows the closed row', async () => {
    open([current, past]);
    clickButton(rowOf('PAST-YEAR'), 'Close');
    await fixture.whenStable();
    const req = http.expectOne(`${API}/api/AcademicYear/2/close`);
    expect(req.request.method).toBe('POST');
    req.flush(envelope(year({ ...past, state: 'CLOSED', isClosed: true })));
    fixture.detectChanges();
    expect(rowOf('PAST-YEAR').textContent).toContain('Closed');
    expect(buttonLabels(rowOf('PAST-YEAR'))).toContain('Reopen');
  });

  it('declining the confirmation sends nothing', async () => {
    open([current, past]);
    confirmAnswer = false;
    clickButton(rowOf('PAST-YEAR'), 'Close');
    await fixture.whenStable();
    http.expectNone(`${API}/api/AcademicYear/2/close`);
  });

  it('Reopen posts to /reopen', async () => {
    open([current, closed]);
    clickButton(rowOf('SHUT-YEAR'), 'Reopen');
    await fixture.whenStable();
    const req = http.expectOne(`${API}/api/AcademicYear/3/reopen`);
    expect(req.request.method).toBe('POST');
    req.flush(envelope(year({ ...closed, state: 'PAST', isClosed: false })));
    fixture.detectChanges();
    expect(rowOf('SHUT-YEAR').textContent).toContain('Past');
  });
});
