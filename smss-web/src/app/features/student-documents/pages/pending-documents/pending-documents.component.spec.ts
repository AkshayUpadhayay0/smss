import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { canAccess } from '../../../../core/config/route-roles';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { PendingDocumentsComponent } from './pending-documents.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const PENDING = {
  totalStudents: 2,
  students: [
    { studentId: 'S1', fullName: 'Asha Rao', admissionNumber: 'ADM1', className: 'Class 1', sectionName: 'A', missing: ['Birth Certificate', 'Aadhaar'] },
    { studentId: 'S2', fullName: 'Ravi Kumar', admissionNumber: 'ADM2', className: null, sectionName: null, missing: ['Aadhaar'] },
  ],
};

describe('PendingDocumentsComponent', () => {
  let fixture: ComponentFixture<PendingDocumentsComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  function open(data = PENDING) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: ['Principal'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PendingDocumentsComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/StudentDocuments/pending`).flush(env(data));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.match(() => true);   // the Manage dialog's own requests
    clearSessions();
  });

  it('lists the students with what each one lacks, and the count', () => {
    open();
    expect(el().querySelector('[data-count="pending"]')!.textContent).toContain('2 students with pending documents');
    const rows = [...el().querySelectorAll('tbody tr')];
    expect(rows.map((r) => r.getAttribute('data-student'))).toEqual(['S1', 'S2']);
    expect(rows[0].textContent).toContain('Birth Certificate');
    expect(rows[0].textContent).toContain('Class 1 – A');
    expect(rows[1].textContent).toContain('—');   // no class on record
  });

  it('searches by name or admission number without another request', () => {
    open();
    cmp()['search'].set('ravi');
    fixture.detectChanges();
    expect([...el().querySelectorAll('tbody tr')].map((r) => r.getAttribute('data-student'))).toEqual(['S2']);
    cmp()['search'].set('zzz');
    fixture.detectChanges();
    expect(el().querySelector('[data-state="empty"]')!.textContent).toContain('Nothing matches');
  });

  it('says so when nobody has a pending document', () => {
    open({ totalStudents: 0, students: [] });
    expect(el().querySelector('[data-state="empty"]')!.textContent).toContain('No student has a pending document');
  });

  it('Manage opens the student\'s documents and the list is read again when it is closed', () => {
    open();
    cmp()['manage'](PENDING.students[0]);
    fixture.detectChanges();
    http.expectOne(`${API}/api/StudentDocuments/S1`);
    cmp()['closeManage']();
    http.expectOne(`${API}/api/StudentDocuments/pending`).flush(env({ totalStudents: 1, students: [PENDING.students[1]] }));
    fixture.detectChanges();
    expect(el().querySelectorAll('tbody tr').length).toBe(1);
  });

  it('only School Admin, Principal and Admission Staff may open the page', () => {
    for (const role of ['School Admin', 'Principal', 'Admission Staff']) expect(canAccess('/student-documents', [role])).toBe(true);
    for (const role of ['Teacher', 'Accountant', 'Student', 'Guardian', 'Super Admin']) expect(canAccess('/student-documents', [role])).toBe(false);
  });
});
