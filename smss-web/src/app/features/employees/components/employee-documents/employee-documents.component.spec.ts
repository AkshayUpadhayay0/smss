import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });
import { EmployeeDocumentsComponent } from './employee-documents.component';

const LOOKUPS = {
  designations: [{ id: 1, name: 'Teacher' }, { id: 2, name: 'Principal' }],
  departments: [{ id: 5, name: 'Science' }],
  employmentStatuses: [{ id: 1, name: 'Active' }, { id: 2, name: 'On Leave' }, { id: 3, name: 'Resigned' }],
  genders: [{ id: 1, name: 'Female' }],
  bloodGroups: [{ id: 1, name: 'O+' }],
  documentTypes: [{ id: 9, name: 'Aadhaar', code: 'AADH', isRequired: true }],
  assignableRoles: [{ roleId: 3, roleName: 'Principal', description: 'Runs the school' }, { roleId: 4, roleName: 'Teacher' }, { roleId: 5, roleName: 'Accountant' }],
};
const EMP = {
  employeeId: 'DPS001EM001', fullName: 'Asha Verma', firstName: 'Asha', middleName: null, lastName: 'Verma', employeeType: 'Teaching' as const,
  designationId: 1, designationName: 'Teacher', departmentId: 5, departmentName: 'Science', employmentStatusId: 1, employmentStatusName: 'Active',
  genderId: 1, genderName: 'Female', bloodGroupId: 1, bloodGroupName: 'O+', dateOfBirth: '1988-05-10', email: 'asha@school.test', mobileNumber: '9876543210',
  alternateMobile: null, address: '12 Park Road', dateOfJoining: '2020-04-01', dateOfLeaving: null, qualification: 'M.Sc', photoUrl: null,
  statusName: 'Active', loginId: null, loginRoles: [] as string[], createdAt: '2026-10-01T00:00:00Z', updatedAt: '2026-10-01T00:00:00Z',
};

const DOC = { employeeDocumentId: 7, documentTypeId: 9, documentTypeName: 'Aadhaar', fileName: 'aadhaar.pdf', contentType: 'application/pdf', fileSize: 2048, remarks: 'front', uploadedByName: 'Admin', uploadedAt: '2026-10-01T10:00:00Z', statusName: 'Active' };

describe('EmployeeDocumentsComponent', () => {
  let fixture: ComponentFixture<EmployeeDocumentsComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;
  const DOCS_URL = `${API}/api/Employee/DPS001EM001/documents`;

  function open(types = LOOKUPS.documentTypes, docs: unknown[] = [DOC]) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(EmployeeDocumentsComponent);
    fixture.componentRef.setInput('employeeId', 'DPS001EM001');
    fixture.componentRef.setInput('documentTypes', types);
    fixture.detectChanges();
    http.expectOne(DOCS_URL).flush(env(docs));
    fixture.detectChanges();
  }
  afterEach(() => { http.verify(); clearSessions(); });
  const pick = (name: string, size = 100) => {
    const f = new File(['x'], name);
    Object.defineProperty(f, 'size', { value: size });
    cmp()['onFile']({ target: { files: [f], value: 'x' } } as unknown as Event);
  };

  it('lists the documents with type, file name, size and uploader', () => {
    open();
    const text = el().textContent ?? '';
    for (const t of ['Aadhaar', 'aadhaar.pdf', '2.0 KB', 'Admin', 'front']) expect(text).toContain(t);
  });

  it('with no employee document types it explains where to add one and offers no upload', () => {
    open([], []);
    expect(el().textContent).toContain('No employee document types are set up yet');
    expect(el().querySelector('input[type="file"]')).toBeNull();
  });

  it('checks the file in the browser first: wrong type, too big, nothing chosen, no type selected', () => {
    open();
    pick('virus.exe');
    expect(cmp()['fileError']()).toContain('Only PDF');
    pick('big.pdf', 6 * 1024 * 1024);
    expect(cmp()['fileError']()).toContain('5 MB');
    cmp()['upload']();
    http.expectNone(DOCS_URL);
    expect(cmp()['typeError']()).toBeTruthy();
  });

  it('uploads a valid file with its type and remarks, adds it to the list and resets the form', () => {
    open([LOOKUPS.documentTypes[0]], []);
    pick('cv.pdf');
    cmp()['typeControl'].setValue('9');
    cmp()['remarksControl'].setValue(' back side ');
    cmp()['upload']();
    const req = http.expectOne(DOCS_URL);
    expect(req.request.method).toBe('POST');
    expect((req.request.body as FormData).get('documentTypeId')).toBe('9');
    expect((req.request.body as FormData).get('remarks')).toBe('back side');
    req.flush(env(DOC, 201));
    fixture.detectChanges();
    expect(cmp()['documents']().length).toBe(1);
    expect(cmp()['typeControl'].value).toBe('');
    expect(cmp()['file']()).toBeNull();
  });

  it('download goes through the authenticated endpoint', () => {
    open();
    const click = vi.fn();
    vi.spyOn(document, 'createElement').mockReturnValueOnce({ click, set href(_: string) {}, set download(_: string) {} } as unknown as HTMLAnchorElement);
    URL.createObjectURL = vi.fn(() => 'blob:x');
    URL.revokeObjectURL = vi.fn();
    cmp()['download'](DOC);
    const req = http.expectOne(`${DOCS_URL}/7/download`);
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['abc']));
    expect(click).toHaveBeenCalled();
  });
});
