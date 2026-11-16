import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { TaxType } from '../../models/tax-type.model';
import { rateRule } from '../../components/tax-type-form-modal/tax-type-form-modal.component';
import { TaxTypeListComponent } from './tax-type-list.component';
import { FormControl } from '@angular/forms';

const URL = `${environment.apiUrl}/api/TaxType`;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });
const tax = (id: number, name: string, rate: number, isActive = true): TaxType => ({ taxTypeId: id, taxName: name, taxCode: name.toUpperCase(), ratePercent: rate, description: null, isActive, createdAt: '', updatedAt: '' });

describe('TaxTypeListComponent', () => {
  let fixture: ComponentFixture<TaxTypeListComponent>;
  let http: HttpTestingController;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;
  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';

  function open(list: TaxType[] | 'fail') {
    fixture = TestBed.createComponent(TaxTypeListComponent);
    fixture.detectChanges();
    const req = http.expectOne((r) => r.url === URL && r.params.get('includeInactive') === 'true');   // the admin screen lists inactive ones too
    if (list === 'fail') req.flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    else req.flush(env(list));
    fixture.detectChanges();
  }

  beforeEach(() => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('shows the empty state when there are no tax types', () => {
    open([]);
    expect(text()).toContain('No tax types yet');
  });

  it('lists name, code, rate and status, active and inactive', () => {
    open([tax(1, 'GST', 5), tax(2, 'Cess', 1.5, false)]);
    expect(text()).toContain('GST');
    expect(text()).toContain('5%');
    expect(text()).toContain('1.5%');
    expect(text()).toContain('Inactive');
    expect(cmp()['total']()).toBe(2);
  });

  it('search filters by name, code or rate', () => {
    open([tax(1, 'GST', 5), tax(2, 'Cess', 1.5)]);
    cmp()['search'].set('cess');
    expect(cmp()['total']()).toBe(1);
    cmp()['search'].set('5%');
    expect(cmp()['total']()).toBe(2);   // "5%" is in "5%" and "1.5%"
    cmp()['search'].set('nothing');
    fixture.detectChanges();
    expect(text()).toContain('Nothing matches your search.');
  });

  it('shows an error state with Try again when loading fails', () => {
    open('fail');
    expect(text()).toContain('Could not load tax types.');
    expect(text()).toContain('Try again');
  });

  it('deactivating asks for confirmation first, then posts to /deactivate; declining sends nothing', async () => {
    open([tax(1, 'GST', 5)]);
    const confirm = TestBed.inject(ConfirmDialogService);
    const spy = vi.spyOn(confirm, 'confirm');
    spy.mockResolvedValueOnce(false);
    await cmp()['toggleStatus'](cmp()['pageRows']()[0]);
    http.expectNone(`${URL}/1/deactivate`);

    spy.mockResolvedValueOnce(true);
    const done = cmp()['toggleStatus'](cmp()['pageRows']()[0]);
    await Promise.resolve();
    await done;
    const req = http.expectOne(`${URL}/1/deactivate`);
    expect(req.request.method).toBe('POST');
    req.flush(env(tax(1, 'GST', 5, false)));
    expect(cmp()['items']()[0].isActive).toBe(false);
  });

  it('there is no delete action, only Edit and Deactivate / Activate', () => {
    open([tax(1, 'GST', 5)]);
    expect(text()).toContain('Deactivate');
    expect(text().toLowerCase()).not.toContain('delete');
  });
});

describe('rateRule', () => {
  const check = (v: string) => rateRule(new FormControl(v));
  it('accepts 0 to 100 with at most 2 decimals', () => {
    for (const ok of ['0', '100', '5', '12.5', '0.01', '99.99']) expect(check(ok)).toBeNull();
  });
  it('rejects out of range and too many decimals', () => {
    for (const bad of ['-1', '100.01', '101', '5.123', 'abc']) expect(check(bad)).not.toBeNull();
  });
});
