import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { canAccess } from '../../../../core/config/route-roles';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { LateFeeReport } from '../../models/fee-report.model';
import { LateFeeReportComponent } from './late-fee-report.component';

const API = environment.apiUrl;
const REPORT = `${API}/api/LateFee/report`;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

const report = (over: Partial<LateFeeReport> = {}): LateFeeReport => ({
  from: '2026-11-01', to: '2026-11-25', classId: null, view: 'charged', page: 1, pageSize: 25, totalItems: 1, chargedCount: 1, chargedAmount: 50, waivedCount: 2, waivedAmount: 120,
  charged: [{ paymentId: 1, receiptNumber: 'RCP-2026-0001', paymentDate: '2026-11-10T08:30:00Z', studentId: 'S1', studentName: 'Asha Rao', className: 'Class 1', feeType: 'Tuition', dueDate: '2026-09-01', daysLate: 70, daysCharged: 65, baseAmount: 1000, amount: 50 }],
  waived: [], ...over,
});

describe('LateFeeReportComponent', () => {
  let fixture: ComponentFixture<LateFeeReportComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  function open(roles: string[] = ['School Admin'], first: LateFeeReport | 'fail' = report()) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'GHS261103002', roles }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(LateFeeReportComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Classes`).flush(env([{ classId: 10, className: 'Class 1', sequenceOrder: 1 }, { classId: 11, className: 'Class 2', sequenceOrder: 2 }]));
    const req = http.expectOne((r) => r.url === REPORT);
    expect(req.request.params.get('view')).toBe('charged');
    if (first === 'fail') req.flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    else req.flush(env(first));
    fixture.detectChanges();
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('lists each charged late fee with the days late and shows charged and waived totals apart', () => {
    open();
    expect(text()).toContain('Asha Rao (S1)');
    expect(text()).toContain('70 (65 charged)');
    expect(text()).toContain('₹50');
    expect(el().querySelector('[data-charged]')?.textContent).toContain('1 · ₹50');
    expect(el().querySelector('[data-waived]')?.textContent).toContain('2 · ₹120');
  });

  it('the Waived tab asks for the waived list and shows what it would have been, who waived and the note', () => {
    open();
    (el().querySelector('[data-tab="waived"]') as HTMLButtonElement).click();
    const req = http.expectOne((r) => r.url === REPORT);
    expect(req.request.params.get('view')).toBe('waived');
    expect(req.request.params.get('page')).toBe('1');
    req.flush(env(report({ view: 'waived', totalItems: 2, charged: [], waived: [{ paymentId: 5, receiptNumber: 'RCP-2026-0005', paymentDate: '2026-11-12T08:30:00Z', studentId: 'S2', studentName: 'Ben', className: 'Class 2', wouldHaveBeen: 60, waivedBy: 'Office', note: 'Poor family' }] })));
    fixture.detectChanges();
    expect(text()).toContain('Would have been');
    expect(text()).toContain('₹60');
    expect(text()).toContain('Poor family');
    expect(text()).toContain('Office');
  });

  it('filters by class and range from the first page and refuses a backwards range without asking', () => {
    open();
    const select = el().querySelector('[data-class]') as HTMLSelectElement;
    select.value = '11'; select.dispatchEvent(new Event('change'));
    const req = http.expectOne((r) => r.url === REPORT);
    expect(req.request.params.get('classId')).toBe('11');
    req.flush(env(report({ classId: 11, charged: [], totalItems: 0, chargedCount: 0, chargedAmount: 0 })));
    fixture.detectChanges();
    expect(text()).toContain('No late fee was charged in this period.');
    cmp()['fromControl'].setValue('2026-11-20'); cmp()['toControl'].setValue('2026-11-01');
    fixture.detectChanges();
    cmp()['apply']();
    http.expectNone((r) => r.url === REPORT);
    expect(text()).toContain("can't be before the start date");
  });

  it('a failed load says so calmly with a retry, and a school with nothing charged shows an empty list, not an error', () => {
    open(['School Admin'], 'fail');
    expect(text()).toContain('Could not load the late fee report.');
    cmp()['load']();
    http.expectOne((r) => r.url === REPORT).flush(env(report({ charged: [], totalItems: 0, chargedCount: 0, chargedAmount: 0, waivedCount: 0, waivedAmount: 0 })));
    fixture.detectChanges();
    expect(text()).toContain('No late fee was charged in this period.');
    expect(el().querySelector('[data-charged]')?.textContent).toContain('0 · ₹0');
  });

  it('only the School Admin, Accountant and Principal open the report', () => {
    for (const r of ['School Admin', 'Accountant', 'Principal']) expect(canAccess('/fee-reports/late-fee', [r])).toBe(true);
    for (const r of ['Teacher', 'Admission Staff', 'Student', 'Guardian', 'Super Admin']) expect(canAccess('/fee-reports/late-fee', [r])).toBe(false);
  });
});
