import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { BackdatingDialogComponent } from './backdating-dialog.component';

const API = environment.apiUrl;
const BASE = `${API}/api/FeeStructure/7/lines/100/backdating`;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

const PREVIEW = {
  feeStructureId: 7, feeTypeId: 100, feeTypeName: 'Tuition', fromDate: '2026-04-01', toDate: '2026-11-17', currentEffectiveFrom: '2026-09-01',
  studentsCount: 2, duesCount: 10, totalAmount: 6000,
  students: [
    { studentId: 'DPS001ST001', studentName: 'Asha Rao', duesCount: 5, amount: 3000 },
    { studentId: 'DPS001ST002', studentName: 'Ravi Kumar', duesCount: 5, amount: 3000 },
  ],
};

describe('BackdatingDialogComponent', () => {
  let fixture: ComponentFixture<BackdatingDialogComponent>;
  let http: HttpTestingController;
  let applied: number;
  let closed: number;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>; // protected members
  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';
  const button = (label: string) => Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((b) => b.textContent?.includes(label)) as HTMLButtonElement;

  beforeEach(async () => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(BackdatingDialogComponent);
    fixture.componentRef.setInput('feeStructureId', 7);
    fixture.componentRef.setInput('feeTypeId', 100);
    fixture.componentRef.setInput('feeTypeName', 'Tuition');
    fixture.componentRef.setInput('currentEffectiveFrom', '2026-09-01');
    fixture.componentRef.setInput('yearStart', '2026-04-01');
    applied = 0;
    closed = 0;
    fixture.componentInstance.applied.subscribe(() => applied++);
    fixture.componentInstance.closed.subscribe(() => closed++);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  function preview() {
    cmp()['showPreview']();
    http.expectOne(`${BASE}/preview`).flush(env(PREVIEW));
    fixture.detectChanges();
  }

  it('starts at the first day of the academic year and writes nothing until applied', () => {
    expect(cmp()['fromDate'].value).toBe('2026-04-01');
    http.expectNone(() => true);
    expect(button('Create earlier dues').disabled).toBe(true);
  });

  it('the preview lists each student with count and amount, and the total', () => {
    preview();
    expect(http.match(() => true)).toHaveLength(0);
    expect(text()).toContain('Asha Rao');
    expect(text()).toContain('Ravi Kumar');
    expect(text()).toContain('₹6,000.00');
    expect(button('Create earlier dues').disabled).toBe(true);   // needs the reason and the tick
  });

  it('refuses a date that is not earlier than where the fee starts now, without calling the API', () => {
    cmp()['fromDate'].setValue('2026-09-01');
    cmp()['showPreview']();
    http.expectNone(() => true);
    expect(cmp()['dateError']()).toContain('before');
  });

  it('applies only with a reason and the confirmation, sending the date and the count the preview showed', () => {
    preview();
    cmp()['confirmed'].setValue(true);
    cmp()['apply']();
    http.expectNone(() => true);   // no reason yet
    expect(cmp()['reasonError']()).toBeTruthy();

    cmp()['reason'].setValue('Fee should have started in April');
    cmp()['apply']();
    const req = http.expectOne(`${BASE}/apply`);
    expect(req.request.body).toEqual({ fromDate: '2026-04-01', reason: 'Fee should have started in April', expectedDuesCount: 10, confirm: true });
    req.flush(env({ backdatingLogId: 1, studentsCount: 2, duesCount: 10, totalAmount: 6000, effectiveFrom: '2026-04-01' }));
    expect(applied).toBe(1);
    expect(closed).toBe(1);
  });

  it('changing the date throws the preview away: it has to be looked at again', () => {
    preview();
    cmp()['confirmed'].setValue(true);
    cmp()['fromDate'].setValue('2026-05-01');
    expect(cmp()['preview']()).toBeNull();
    expect(cmp()['confirmed'].value).toBe(false);
  });

  it('with nothing to create it says so and offers no apply', () => {
    cmp()['showPreview']();
    http.expectOne(`${BASE}/preview`).flush(env({ ...PREVIEW, duesCount: 0, totalAmount: 0, studentsCount: 0, students: [] }));
    fixture.detectChanges();
    expect(text()).toContain('Nothing to create');
    expect(cmp()['canApply']()).toBe(false);
  });
});
