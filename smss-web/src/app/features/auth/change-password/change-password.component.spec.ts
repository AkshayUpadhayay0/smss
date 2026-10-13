import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { clearSessions, makeLoginResponse, makeUser, seedSession } from '../../../core/testing/auth-test-utils';
import { ChangePasswordComponent } from './change-password.component';

const URL = `${environment.apiUrl}/api/Auth/change-password`;

describe('ChangePasswordComponent landing page', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<ChangePasswordComponent>;
  let navigateByUrl: ReturnType<typeof vi.spyOn>;

  function setup(user: Parameters<typeof makeUser>[0]) {
    clearSessions();
    seedSession({ user: makeUser(user) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(ChangePasswordComponent);
    fixture.detectChanges();
  }

  function changePassword(updatedUser: Parameters<typeof makeUser>[0]) {
    const cmp = fixture.componentInstance as unknown as { form: { setValue(v: object): void }; submit(): void };
    cmp.form.setValue({ currentPassword: 'Temp#1234', newPassword: 'NewPass@12345', confirmPassword: 'NewPass@12345' });
    cmp.submit();
    http.expectOne(URL).flush({
      status: true,
      statusCode: 200,
      message: 'ok',
      data: makeLoginResponse({ user: makeUser({ ...updatedUser, isFirstLogin: false }) }),
    });
  }

  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('sends a School Admin to Import Masters after the first password change', () => {
    setup({ isFirstLogin: true, roles: ['School Admin'], userType: 'School Id' });
    changePassword({ roles: ['School Admin'], userType: 'School Id' });
    expect(navigateByUrl).toHaveBeenCalledWith('/school-setup/import-masters');
  });

  it('sends a School Admin who is only changing a password later to the dashboard', () => {
    setup({ isFirstLogin: false, roles: ['School Admin'], userType: 'School Id' });
    changePassword({ roles: ['School Admin'], userType: 'School Id' });
    expect(navigateByUrl).toHaveBeenCalledWith('/dashboard');
  });

  it('sends a first-login user who cannot open School Setup (e.g. a student) to the dashboard', () => {
    setup({ isFirstLogin: true, roles: ['Student'], userType: 'Student' });
    changePassword({ roles: ['Student'], userType: 'Student' });
    expect(navigateByUrl).toHaveBeenCalledWith('/dashboard');
  });
});
