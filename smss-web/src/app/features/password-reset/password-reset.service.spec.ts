import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { PasswordResetService } from './password-reset.service';

describe('PasswordResetService', () => {
  it('posts only the login id and the trimmed reason (never a school id) and unwraps the result', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    let got: unknown;
    TestBed.inject(PasswordResetService).reset('DPS001ST001', ' left it at home ').subscribe((r) => (got = r));
    const req = http.expectOne(`${environment.apiUrl}/api/PasswordReset`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ loginId: 'DPS001ST001', reason: 'left it at home' });
    req.flush({ status: true, statusCode: 200, message: 'ok', data: { loginId: 'DPS001ST001', displayName: 'Abhinav', temporaryPassword: 'x' } });
    expect(got).toEqual({ loginId: 'DPS001ST001', displayName: 'Abhinav', temporaryPassword: 'x' });
    http.verify();
  });
});
