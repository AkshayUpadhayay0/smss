import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { canAccess } from '../../../../core/config/route-roles';
import { LateFeeRule } from '../../models/late-fee.model';
import { LateFeeComponent } from './late-fee.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const RULE = (over: Partial<LateFeeRule> = {}): LateFeeRule => ({
  enabled: false, ruleType: null, amount: null, percent: null, graceDays: 0, capPerDue: null, taxApplies: false, updatedAt: null, updatedBy: null,
  feeTypes: [{ feeTypeId: 1, name: 'Tuition', isActive: true, appliesLateFee: false }, { feeTypeId: 2, name: 'Bus', isActive: true, appliesLateFee: false }], ...over,
});

describe('LateFeeComponent', () => {
  let fixture: ComponentFixture<LateFeeComponent>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;
  const URL = `${API}/api/LateFee/rule`;

  function open(r: LateFeeRule | 'fail') {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(LateFeeComponent);
    fixture.detectChanges();
    const req = http.expectOne(URL);
    if (r === 'fail') req.flush(null, { status: 500, statusText: 'x' }); else req.flush(env(r));
    fixture.detectChanges();
  }
  const pick = (sel: string, v: string) => { const e = root().querySelector(sel) as HTMLInputElement | HTMLSelectElement; e.value = v; e.dispatchEvent(new Event(e.tagName === 'SELECT' ? 'change' : 'input')); fixture.detectChanges(); };
  const tick = (sel: string) => { (root().querySelector(sel) as HTMLInputElement).click(); fixture.detectChanges(); };
  const save = () => { cmp()['save'](); fixture.detectChanges(); };
  afterEach(() => http.verify());

  it('starts everything off for a school that never set a rule, and says a new rule applies from the next payment', () => {
    open(RULE());
    expect((root().querySelector('[data-enabled]') as HTMLInputElement).checked).toBe(false);
    expect(root().querySelectorAll('[data-types] input:checked').length).toBe(0);
    expect(root().querySelector('[data-note]')?.textContent).toContain('next payment');
  });

  it('tells a mistake before sending anything', () => {
    open(RULE());
    tick('[data-enabled]');
    save();
    http.expectNone(URL);
    expect(root().querySelector('[data-errors]')?.textContent).toContain('how the late fee is worked out');
    pick('[data-type]', 'FLAT');
    save();
    expect(root().querySelector('[data-errors]')?.textContent).toContain('flat late fee amount');
    pick('[data-amount]', '50');
    save();
    expect(root().querySelector('[data-errors]')?.textContent).toContain('at least one fee type');
    http.expectNone(URL);
  });

  it('saves the whole rule and the chosen fee types, with the figure of the chosen type only', () => {
    open(RULE());
    tick('[data-enabled]');
    pick('[data-type]', 'PERCENT');
    pick('[data-percent]', '10');
    pick('[data-grace]', '5');
    pick('[data-cap]', '100');
    tick('[data-tax]');
    tick('[data-type-id="1"]');
    cmp()['save']();
    const req = http.expectOne(URL);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ enabled: true, ruleType: 'PERCENT', amount: null, percent: 10, graceDays: 5, capPerDue: 100, taxApplies: true, feeTypeIds: [1] });
    req.flush(env(RULE({ enabled: true, ruleType: 'PERCENT', percent: 10, graceDays: 5, capPerDue: 100, taxApplies: true, updatedAt: '2026-11-25T10:00:00Z', updatedBy: 'SCH1', feeTypes: [{ feeTypeId: 1, name: 'Tuition', isActive: true, appliesLateFee: true }, { feeTypeId: 2, name: 'Bus', isActive: true, appliesLateFee: false }] })));
    fixture.detectChanges();
    expect(root().querySelector('[data-audit]')?.textContent).toContain('SCH1');
    expect((root().querySelector('[data-type-id="1"]') as HTMLInputElement).checked).toBe(true);
  });

  it('a disabled rule can be saved without a figure (switching the late fee off)', () => {
    open(RULE({ enabled: true, ruleType: 'FLAT', amount: 50, feeTypes: [{ feeTypeId: 1, name: 'Tuition', isActive: true, appliesLateFee: true }] }));
    tick('[data-enabled]');
    cmp()['save']();
    expect(http.expectOne(URL).request.body.enabled).toBe(false);
  });

  it('a failed load shows a calm message, and only the School Admin opens the page', () => {
    open('fail');
    expect(root().querySelector('[data-failed]')).not.toBeNull();
    expect(canAccess('/fee-setup/late-fee', ['School Admin'])).toBe(true);
    for (const r of ['Principal', 'Accountant', 'Teacher', 'Admission Staff', 'Student', 'Guardian', 'Super Admin']) expect(canAccess('/fee-setup/late-fee', [r])).toBe(false);
  });
});
