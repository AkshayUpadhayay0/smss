import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { ExistingStudentFormComponent } from './existing-student-form.component';

const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const TYPES = [
  { admissionTypeId: 7, typeCode: 'NEW_ADMISSION', typeName: 'New Admission' },
  { admissionTypeId: 8, typeCode: 'EXISTING_STUDENT', typeName: 'Existing Student' },
];

describe('ExistingStudentFormComponent admission type', () => {
  let fixture: ComponentFixture<ExistingStudentFormComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;

  function open(types: unknown[]) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ExistingStudentFormComponent);
    fixture.detectChanges();
    for (let round = 0; round < 6; round++) {
      const pending = http.match(() => true);
      if (!pending.length) break;
      for (const req of pending) req.flush(env(req.request.url === `${environment.apiUrl}/api/AdmissionTypes` ? types : []));
      fixture.detectChanges();
    }
  }
  afterEach(() => {
    http?.verify();
    clearSessions();
  });

  const typeField = () => [...el().querySelectorAll('app-input')].find((i) => i.textContent?.includes('Admission type'))!.querySelector('input') as HTMLInputElement;

  it('shows "Existing Student" as the admission type, read-only, and no banner', () => {
    open(TYPES);
    expect(typeField().value).toBe('Existing Student');
    expect(typeField().disabled).toBe(true);
    expect(el().querySelector('.banner')).toBeNull();
  });

  it('the type is found by its CODE: a list without EXISTING_STUDENT (even one named "Existing Student") shows the contact-support banner', () => {
    open([{ admissionTypeId: 9, typeCode: 'SOMETHING_ELSE', typeName: 'Existing Student' }]);
    expect(el().querySelector('.banner')?.textContent).toContain('contact support');
  });
});
