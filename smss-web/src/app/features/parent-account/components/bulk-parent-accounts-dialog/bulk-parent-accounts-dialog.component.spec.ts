import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { BulkParentAccountsDialogComponent } from './bulk-parent-accounts-dialog.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

describe('BulkParentAccountsDialogComponent', () => {
  let fixture: ComponentFixture<BulkParentAccountsDialogComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const create = () => el().querySelector('[data-action="create"] button') as HTMLButtonElement;

  function open(eligible = 105) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'GHS261103002', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(BulkParentAccountsDialogComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/ParentAccounts/bulk/preview`).flush(env({ eligible, willCreate: Math.min(eligible, 100), maxPerRun: 100, incompleteDetails: 7, withoutConsentOnRecord: 105 }));
    fixture.detectChanges();
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('shows the counts first, with the cap, and creates nothing by itself', () => {
    open();
    expect(el().querySelector('[data-field="eligible"]')?.textContent).toBe('105');
    expect(el().querySelector('[data-field="will-create"]')?.textContent).toContain('100');
    expect(el().querySelector('[data-field="incomplete"]')?.textContent).toBe('7');
    http.expectNone(`${API}/api/ParentAccounts/bulk/create`);
  });

  it('the button stays off until the School Admin confirms the school holds parent consent, then posts the confirmation', () => {
    open();
    expect(create().disabled).toBe(true);
    (el().querySelector('[data-field="consent"]') as HTMLInputElement).click();
    fixture.detectChanges();
    expect(create().disabled).toBe(false);
    create().click();
    const req = http.expectOne(`${API}/api/ParentAccounts/bulk/create`);
    expect(req.request.body).toEqual({ confirmSchoolHoldsParentConsent: true });
    req.flush(env({ attempted: 100, created: 99, notCreated: 1, remaining: 5, items: [{ guardianId: 3, name: 'Meera', outcome: 'NOT_CREATED', reason: 'This school has no short code.' }] }));
    fixture.detectChanges();
    expect(el().querySelector('[data-state="result"]')?.textContent).toContain('99');
    expect(el().querySelector('[data-state="result"]')?.textContent).toContain('5 more are waiting');
    expect(el().querySelector('[data-list="problems"]')?.textContent).toContain('Meera: This school has no short code.');
  });

  it('with nobody eligible there is no consent box and nothing to create', () => {
    open(0);
    expect(el().querySelector('[data-field="consent"]')).toBeNull();
    expect(create().disabled).toBe(true);
  });
});
