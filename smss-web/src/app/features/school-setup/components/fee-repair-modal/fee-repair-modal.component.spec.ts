import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { AcademicYear } from '../../models/academic-year.model';
import { FeeRepairPreview, FeeRepairResult } from '../../models/fee-repair.model';
import { FeeRepairModalComponent } from './fee-repair-modal.component';

const API = environment.apiUrl;
const envelope = (data: unknown) => ({ status: true, statusCode: 200, message: 'ok', data });
const YEAR = { academicYearId: 9, yearName: '2026-27' } as AcademicYear;
const PREVIEW = (over: Partial<FeeRepairPreview> = {}): FeeRepairPreview => ({ academicYearId: 9, yearName: '2026-27', missingCount: 3, wouldAssign: 2, maxPerRun: 500, yearStart: '2026-04-01', yearStarted: true, backdatingAllowed: true, classesWithoutStructure: [{ className: 'Class 2', students: 1 }], ...over });
const RESULT = (over: Partial<FeeRepairResult> = {}): FeeRepairResult => ({ academicYearId: 9, yearName: '2026-27', assigned: 2, skippedNoStructure: 1, remaining: 0, backdated: false, failed: [], ...over });

describe('FeeRepairModalComponent', () => {
  let fixture: ComponentFixture<FeeRepairModalComponent>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;
  const btn = () => root().querySelector('[data-assign-btn] button') as HTMLButtonElement;

  function open(p: FeeRepairPreview | 'fail') {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(FeeRepairModalComponent);
    fixture.componentRef.setInput('year', YEAR);
    fixture.detectChanges();
    const req = http.expectOne(`${API}/api/FeeRepair/years/9/missing`);
    if (p === 'fail') req.flush(null, { status: 500, statusText: 'x' }); else req.flush(envelope(p));
    fixture.detectChanges();
  }
  afterEach(() => http.verify());

  it('shows how many students have no fees and which classes have no structure, and saves nothing yet', () => {
    open(PREVIEW());
    expect(root().querySelector('[data-count]')?.textContent).toContain('3');
    expect(root().querySelector('[data-no-structure]')?.textContent).toContain('Class 2: 1 student');
    http.expectNone(`${API}/api/FeeRepair/years/9/assign`);
  });

  it('assigns on the button (a POST), then lists the students it could not do', () => {
    open(PREVIEW());
    btn().click();
    const req = http.expectOne(`${API}/api/FeeRepair/years/9/assign`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ chargeFromYearStart: false, confirmBackdating: false, reason: null });   // nothing is back-dated unless chosen
    req.flush(envelope(RESULT({ assigned: 1, failed: [{ studentId: 'ZR1', studentName: 'Stu ZR1', className: 'Class 1', reason: 'Could not be assigned' }] })));
    fixture.detectChanges();
    expect(root().querySelector('[data-done]')?.textContent).toContain('1 student');
    expect(root().querySelector('[data-failed]')?.textContent).toContain('Stu ZR1');
  });

  it('says so when a capped run leaves students for another run', () => {
    open(PREVIEW({ wouldAssign: 500, missingCount: 700 }));
    btn().click();
    http.expectOne(`${API}/api/FeeRepair/years/9/assign`).flush(envelope(RESULT({ assigned: 500, remaining: 200 })));
    fixture.detectChanges();
    expect(root().querySelector('[data-remaining]')?.textContent).toContain('200');
  });

  it('with nobody to do, there is nothing to press; a failed check shows a calm message', () => {
    open(PREVIEW({ missingCount: 0, wouldAssign: 0, classesWithoutStructure: [] }));
    expect(root().querySelector('[data-none]')).not.toBeNull();
    expect(btn().disabled).toBe(true);
  });

  it('a failed check never shows technical detail', () => {
    open('fail');
    expect(root().querySelector('[role="alert"]')?.textContent).toContain('could not be checked');
  });

  it('charging from the year start needs the confirmation and a reason, then sends them', () => {
    open(PREVIEW());
    (root().querySelector('[data-from-start]') as HTMLInputElement).click();
    fixture.detectChanges();
    expect(btn().disabled).toBe(true);
    (root().querySelector('[data-confirm-backdating]') as HTMLInputElement).click();
    fixture.detectChanges();
    expect(btn().disabled).toBe(true);   // still no reason
    const reason = root().querySelector('[data-reason]') as HTMLTextAreaElement;
    reason.value = ' Fees were missed '; reason.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(btn().disabled).toBe(false);
    btn().click();
    const req = http.expectOne(`${API}/api/FeeRepair/years/9/assign`);
    expect(req.request.body).toEqual({ chargeFromYearStart: true, confirmBackdating: true, reason: 'Fees were missed' });
    req.flush(envelope(RESULT({ backdated: true })));
  });

  it('offers nothing to back-date when the year has not started or is not the current one', () => {
    open(PREVIEW({ yearStarted: false }));
    expect(root().querySelector('[data-from-start]')).toBeNull();
  });

  it('a started year that is not current says fees are charged from today', () => {
    open(PREVIEW({ backdatingAllowed: false }));
    expect(root().querySelector('[data-from-start]')).toBeNull();
    expect(root().querySelector('[data-no-backdating]')).not.toBeNull();
  });
});
