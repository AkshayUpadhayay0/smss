import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { StudentTransportCardComponent } from './student-transport-card.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const page = <T>(items: T[]) => ({ items, total: items.length, page: 1, pageSize: 100 });

const BASE = { studentId: 'DPS001ST001', studentName: 'Asha Kid', academicYearId: 5, academicYearName: '2026-27', isCurrentYear: true };
const NONE = { ...BASE, assignment: null };
const ASSIGNED = { ...BASE, assignment: { areaId: 1, areaName: 'Civil Lines', areaIsActive: true } };
const AREAS = page([{ areaId: 1, areaName: 'Civil Lines' }, { areaId: 2, areaName: 'Ring Road' }]);

describe('StudentTransportCardComponent', () => {
  let fixture: ComponentFixture<StudentTransportCardComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => el().textContent ?? '';
  const buttons = () => [...el().querySelectorAll('app-button')].map((b) => b.textContent?.trim());
  const cmp = () => fixture.componentInstance as unknown as { areaControl: { setValue(v: string): void }; save(): void; remove(): Promise<void> };

  function create(roles: string[]) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StudentTransportCardComponent);
    fixture.componentRef.setInput('studentId', 'DPS001ST001');
    fixture.detectChanges();
  }

  function open(roles: string[], current: unknown = NONE) {
    create(roles);
    http.expectOne(`${API}/api/StudentTransport/DPS001ST001`).flush(env(current));
    if (roles.includes('School Admin')) http.expectOne((r) => r.url === `${API}/api/TransportAreas` && r.params.get('status') === 'Active').flush(env(AREAS));
    fixture.detectChanges();
  }

  afterEach(() => {
    http?.verify();
    clearSessions();
  });

  it('School Admin sees the current area, the Active areas to choose from, and can assign', () => {
    open(['School Admin']);
    expect(text()).toContain('Not using school transport');
    expect([...el().querySelectorAll('option')].map((o) => o.textContent?.trim())).toEqual(expect.arrayContaining(['Civil Lines', 'Ring Road']));
    expect(buttons().some((b) => b?.includes('Assign area'))).toBe(true);
    cmp().areaControl.setValue('2');
    cmp().save();
    const req = http.expectOne(`${API}/api/StudentTransport/DPS001ST001`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ areaId: 2 });
    req.flush(env({ ...BASE, assignment: { areaId: 2, areaName: 'Ring Road', areaIsActive: true } }));
    fixture.detectChanges();
    expect(text()).toContain('Ring Road');
  });

  it('shows the assigned area and offers Change area and Remove', () => {
    open(['School Admin'], ASSIGNED);
    expect(text()).toContain('Civil Lines');
    expect(buttons().some((b) => b?.includes('Remove'))).toBe(true);
    expect(buttons().some((b) => b?.includes('Change area'))).toBe(true);
  });

  it('Remove asks first: declining sends nothing, confirming POSTs remove', async () => {
    open(['School Admin'], ASSIGNED);
    const confirm = vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValueOnce(false).mockResolvedValueOnce(true);
    await cmp().remove();
    http.expectNone(`${API}/api/StudentTransport/DPS001ST001/remove`);
    const done = cmp().remove();
    await Promise.resolve();
    await Promise.resolve();
    const req = http.expectOne(`${API}/api/StudentTransport/DPS001ST001/remove`);
    expect(req.request.method).toBe('POST');
    req.flush(env(NONE));
    await done;
    fixture.detectChanges();
    expect(text()).toContain('Not using school transport');
    expect(confirm).toHaveBeenCalledTimes(2);
  });

  it('warns when the assigned area was deactivated', () => {
    open(['School Admin'], { ...BASE, assignment: { areaId: 1, areaName: 'Civil Lines', areaIsActive: false } });
    expect(text()).toContain('Area inactive');
    expect(text()).toContain('Choose another area');
  });

  it('Principal sees it READ-ONLY: no dropdown, no Assign / Remove, and the areas are not even requested', () => {
    open(['Principal'], ASSIGNED);
    expect(text()).toContain('Civil Lines');
    expect(text()).toContain('Only a School Admin can change this');
    expect(el().querySelector('select')).toBeNull();
    expect(buttons().some((b) => b?.includes('Assign') || b?.includes('Remove') || b?.includes('Change'))).toBe(false);
  });

  it.each(['Accountant', 'Teacher', 'Student', 'Admission Staff'])('%s: nothing is shown and nothing is requested', (role) => {
    create([role]);
    expect(el().querySelector('app-card')).toBeNull();
    http.expectNone(() => true);
  });

  it('says so, with Try again, when the details cannot be loaded', () => {
    create(['School Admin']);
    http.expectOne(`${API}/api/StudentTransport/DPS001ST001`).flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'err' });
    fixture.detectChanges();
    expect(text()).toContain('Could not load the transport details');
    expect(buttons()).toContain('Try again');
  });
});
