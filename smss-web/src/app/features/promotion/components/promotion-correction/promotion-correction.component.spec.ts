import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { AcademicYear } from '../../../school-setup/models/academic-year.model';
import { PromotionCorrectionCheck } from '../../models/promotion.model';
import { PromotionCorrectionComponent } from './promotion-correction.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const YEARS = [{ academicYearId: 2, yearName: '2026-27' }, { academicYearId: 3, yearName: '2027-28' }] as AcademicYear[];
const CHECK = (over: Partial<PromotionCorrectionCheck> = {}): PromotionCorrectionCheck => ({
  studentId: 'S1', studentName: 'Asha Rao', sourceYearName: '2026-27', decision: 'PROMOTE', outcome: 'PROMOTED', canCorrect: true, blockers: [],
  willRemove: { newYearName: '2027-28', className: 'Class 2', sectionName: 'A', rollNumber: '1', enrolment: true, feeItems: 2, dues: 12, duesAmount: 24000, feeStructure: true }, ...over,
});

describe('PromotionCorrectionComponent', () => {
  let fixture: ComponentFixture<PromotionCorrectionComponent>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;
  const type = (sel: string, v: string) => { const e = root().querySelector(sel) as HTMLInputElement; e.value = v; e.dispatchEvent(new Event('input')); fixture.detectChanges(); };
  const answer = (b: boolean) => { (TestBed.inject(ConfirmDialogService) as unknown as { confirm: () => Promise<boolean> }).confirm = () => Promise.resolve(b); };

  function open() {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PromotionCorrectionComponent);
    fixture.componentRef.setInput('years', YEARS);
    fixture.componentRef.setInput('defaultYearId', 2);
    fixture.detectChanges();
  }
  function check(c: PromotionCorrectionCheck) {
    type('[data-student]', 'S1');
    (root().querySelector('[data-check-btn] button') as HTMLButtonElement).click();
    const req = http.expectOne((r) => r.url === `${API}/api/Promotion/corrections/check`);
    expect(req.request.params.get('studentId')).toBe('S1');
    expect(req.request.params.get('sourceYearId')).toBe('2');
    req.flush(env(c));
    fixture.detectChanges();
  }
  afterEach(() => http.verify());

  it('asks nothing until the admin checks a student, then shows what would be removed', () => {
    open();
    http.expectNone((r) => r.url.includes('/api/Promotion'));
    check(CHECK());
    expect(root().querySelector('[data-check]')?.textContent).toContain('2 fee items');
    expect(root().querySelector('[data-check]')?.textContent).toContain('Class 2 A');
    expect((root().querySelector('[data-correct-btn] button') as HTMLButtonElement).disabled).toBe(true);   // no reason yet
  });

  it('a refusal lists plainly what blocks it and offers no correct button', () => {
    open();
    check(CHECK({ canCorrect: false, blockers: ['3 attendance records exist in 2027-28.'] }));
    expect(root().querySelector('[data-blockers]')?.textContent).toContain('3 attendance records');
    expect(root().querySelector('[data-correct-btn]')).toBeNull();
  });

  it('needs a reason of 10 characters and a confirmation, then sends the correction', async () => {
    open();
    check(CHECK());
    type('[data-reason]', 'too short');
    expect((root().querySelector('[data-correct-btn] button') as HTMLButtonElement).disabled).toBe(true);
    type('[data-reason]', 'Wrong class by mistake');
    answer(false);
    (root().querySelector('[data-correct-btn] button') as HTMLButtonElement).click();
    await new Promise((r) => setTimeout(r));
    http.expectNone(`${API}/api/Promotion/corrections`);
    answer(true);
    (root().querySelector('[data-correct-btn] button') as HTMLButtonElement).click();
    await new Promise((r) => setTimeout(r));
    const req = http.expectOne(`${API}/api/Promotion/corrections`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ studentId: 'S1', sourceYearId: 2, reason: 'Wrong class by mistake', confirm: true });
    req.flush(env({ correctionId: 1, studentId: 'S1', outcomeCleared: 'PROMOTED', removed: CHECK().willRemove }));
    fixture.detectChanges();
    expect(root().querySelector('[data-done]')?.textContent).toContain('can be promoted again');
    expect(root().querySelector('[data-check]')).toBeNull();
  });

  it('a failed check shows a calm message', () => {
    open();
    type('[data-student]', 'S1');
    (root().querySelector('[data-check-btn] button') as HTMLButtonElement).click();
    http.expectOne((r) => r.url === `${API}/api/Promotion/corrections/check`).flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(root().querySelector('[data-failed]')).not.toBeNull();
  });
});
