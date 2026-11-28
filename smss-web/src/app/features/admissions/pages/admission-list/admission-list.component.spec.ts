import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { YearContextService } from '../../../../core/services/year-context.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { AdmissionListComponent } from './admission-list.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const yr = (id: number, name: string, isCurrent: boolean) => ({ academicYearId: id, schoolId: 'S', yearName: name, startDate: '2026-04-01', endDate: '2027-03-31', isCurrent, statusName: 'Active', state: isCurrent ? 'CURRENT' : 'PAST', isClosed: false, usage: {}, createdAt: '', updatedAt: '' });
const app = (id: number, name: string, year: number) => ({ admissionId: id, applicationNumber: `APP${id}`, applicantFullName: name, statusName: 'APPLICATION_SUBMITTED', applicationDate: '2026-11-01', academicYearId: year, academicYearName: year === 7 ? '2026-27' : '2025-26', appliedClassId: 1, appliedClassName: 'Class 1', admissionTypeId: 1, admissionTypeName: 'New' });

describe('AdmissionListComponent follows the header viewing year', () => {
  let http: HttpTestingController;
  const listReqs = () => http.match((r) => r.url === `${API}/api/Admission/S`);

  function open(roles: string[]) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'School Id', roles }) });
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(AdmissionListComponent);
    fixture.detectChanges();
    return fixture;
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('waits for the viewing year, then asks for that year with the explicit id; another year asks again; there is no year filter of its own', () => {
    const f = open(['School Admin']);
    expect(listReqs()).toHaveLength(0);                                        // nothing is requested for a year nobody chose yet
    http.match(`${API}/api/AcademicYear`).forEach((r) => r.flush(env([yr(6, '2025-26', false), yr(7, '2026-27', true)])));
    TestBed.tick();
    f.detectChanges();
    const [first] = listReqs();
    expect(first.request.params.get('academicYearId')).toBe('7');
    first.flush(env([app(1, 'Asha', 7)]));
    TestBed.inject(YearContextService).select('6');
    TestBed.tick();
    const [second] = listReqs();
    expect(second.request.params.get('academicYearId')).toBe('6');
    second.flush(env([app(2, 'Bela', 6)]));
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Bela');
    expect(f.nativeElement.querySelector('app-select[placeholder="All years"]')).toBeNull();
    expect(f.nativeElement.querySelector('[data-year-note]')?.textContent).toContain('2025-26');
  });

  it('a role without the selector (Admission Staff) lists every year, as before', () => {
    const f = open(['Admission Staff']);
    const [req] = listReqs();
    expect(req.request.params.has('academicYearId')).toBe(false);
    req.flush(env([app(1, 'Asha', 7), app(2, 'Bela', 6)]));
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Asha');
    expect(f.nativeElement.textContent).toContain('Bela');
  });
});
