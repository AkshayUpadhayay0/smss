import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../core/testing/auth-test-utils';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });
import { EmployeeService } from './employee.service';

describe('EmployeeService', () => {
  let http: HttpTestingController;
  let service: EmployeeService;
  beforeEach(() => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(EmployeeService);
  });
  afterEach(() => { http.verify(); clearSessions(); });

  it('list sends paging + only the filters that are set ("All" and blanks are not sent)', () => {
    service.list({ search: ' ', designationId: '2', status: 'All', employeeType: '' }, 3, 25).subscribe();
    const req = http.expectOne((r) => r.url === `${API}/api/Employee`);
    expect(req.request.params.get('page')).toBe('3');
    expect(req.request.params.get('pageSize')).toBe('25');
    expect(req.request.params.get('designationId')).toBe('2');
    for (const k of ['status', 'employeeType', 'departmentId', 'employmentStatusId']) expect(req.request.params.has(k)).toBe(false);
    req.flush(env({ items: [], total: 0, page: 3, pageSize: 25 }));
  });

  it('createLogin posts the chosen role ids; uploadDocument sends the type and remarks as form fields next to the file', () => {
    service.createLogin('DPS001EM001', [3, 4]).subscribe();
    const login = http.expectOne(`${API}/api/Employee/DPS001EM001/create-login`);
    expect(login.request.body).toEqual({ roleIds: [3, 4] });
    login.flush(env({}));

    service.uploadDocument('DPS001EM001', new File(['%PDF-'], 'a.pdf'), 9, 'front').subscribe();
    const up = http.expectOne(`${API}/api/Employee/DPS001EM001/documents`);
    const form = up.request.body as FormData;
    expect(form.get('documentTypeId')).toBe('9');
    expect(form.get('remarks')).toBe('front');
    expect((form.get('file') as File).name).toBe('a.pdf');
    up.flush(env({}));
  });

  it('downloads a document as a blob through the authenticated endpoint', () => {
    service.downloadDocument('DPS001EM001', 7).subscribe((b) => expect(b.size).toBe(3));
    const req = http.expectOne(`${API}/api/Employee/DPS001EM001/documents/7/download`);
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['abc']));
  });
});
