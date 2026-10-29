import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { SchoolSubscriptionRow, SubscriptionPricing } from '../../models/super-admin-dashboard.model';
import { ConfigureBillingModalComponent } from './configure-billing-modal.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

const school = (o: Partial<SchoolSubscriptionRow> = {}): SchoolSubscriptionRow => ({
  schoolId: 'S1', schoolCode: 'C1', schoolName: 'Alpha School', schoolStatusId: 1, subscriptionStatus: 'TRIAL', billingFrequency: null,
  trialEndDate: '2026-11-15', trialExpired: false, nextRenewalDate: null, customRatePerStudentMonthly: null, customYearlyDiscountPercent: null,
  includeStaffInBilling: false, studentCount: 3, teacherCount: 2, staffCount: 1, billableUserCount: 3, ...o,
});
const pricing: SubscriptionPricing = { pricingId: 1, ratePerStudentMonthly: 20, yearlyDiscountPercent: 10, trialDays: 30, effectiveFrom: '2026-10-01', isActive: true };

describe('ConfigureBillingModalComponent', () => {
  let fixture: ComponentFixture<ConfigureBillingModalComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const cmp = () => fixture.componentInstance as unknown as {
    form: { controls: Record<'frequency' | 'customRate' | 'customDiscount' | 'includeStaff', { setValue(v: unknown): void }> };
    submit(): void;
  };

  function open(mode: 'configure' | 'terms', s: SchoolSubscriptionRow = school()): void {
    clearSessions();
    seedSession({ user: makeUser() });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ConfigureBillingModalComponent);
    fixture.componentRef.setInput('school', s);
    fixture.componentRef.setInput('mode', mode);
    fixture.componentRef.setInput('pricing', pricing);
    fixture.detectChanges();
  }

  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('configure: shows the global defaults as placeholders and asks for a frequency', () => {
    open('configure');
    const placeholders = Array.from(el().querySelectorAll('input[type=number]')).map((i) => i.getAttribute('placeholder'));
    expect(placeholders).toEqual(['Global: ₹20.00', 'Global: 10%']);
    expect(text()).toContain('Include teachers and staff in billing');
    expect(text()).toContain('Billing frequency');
    cmp().submit();
    fixture.detectChanges();
    expect(text()).toContain('Choose a billing frequency');
  });

  it('configure with blank custom fields sends nulls (= use the global terms) and staff off', () => {
    open('configure');
    cmp().form.controls.frequency.setValue('MONTHLY');
    cmp().submit();
    const req = http.expectOne(`${API}/api/SchoolSubscription/S1/configure`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ billingFrequency: 'MONTHLY', customRatePerStudentMonthly: null, customYearlyDiscountPercent: null, includeStaffInBilling: false });
    req.flush(env({}));
  });

  it('configure with a custom rate, a custom discount and staff included sends them as numbers', () => {
    open('configure');
    cmp().form.controls.frequency.setValue('YEARLY');
    cmp().form.controls.customRate.setValue('15'); // a number input hands back text
    cmp().form.controls.customDiscount.setValue('25');
    cmp().form.controls.includeStaff.setValue(true);
    cmp().submit();
    const req = http.expectOne(`${API}/api/SchoolSubscription/S1/configure`);
    expect(req.request.body).toEqual({ billingFrequency: 'YEARLY', customRatePerStudentMonthly: 15, customYearlyDiscountPercent: 25, includeStaffInBilling: true });
    req.flush(env({}));
  });

  it('refuses a negative rate and a discount above 100 without calling the API', () => {
    open('configure');
    cmp().form.controls.frequency.setValue('YEARLY');
    cmp().form.controls.customRate.setValue('-1');
    cmp().form.controls.customDiscount.setValue('101');
    cmp().submit();
    fixture.detectChanges();
    expect(text()).toContain('The rate cannot be negative');
    expect(text()).toContain('Enter a percentage from 0 to 100');
  });

  it('terms mode: no frequency field, starts from the school\'s current terms, saves with PUT', () => {
    open('terms', school({ subscriptionStatus: 'ACTIVE', billingFrequency: 'MONTHLY', customRatePerStudentMonthly: 15, includeStaffInBilling: true }));
    expect(text()).not.toContain('Billing frequency');
    expect(text()).toContain('Billing terms: Alpha School');
    expect(text()).toContain('Invoices already generated keep the numbers they were issued with.');
    const rate = el().querySelector('input[type=number]') as HTMLInputElement;
    expect(rate.value).toBe('15');
    cmp().form.controls.customRate.setValue(''); // cleared = back to the global rate
    cmp().submit();
    const req = http.expectOne(`${API}/api/SchoolSubscription/S1/terms`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ customRatePerStudentMonthly: null, customYearlyDiscountPercent: null, includeStaffInBilling: true });
    req.flush(env({}));
  });
});
