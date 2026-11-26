import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { DashboardPageComponent } from './dashboard-page.component';

function open(roles: string[]) {
  clearSessions();
  seedSession({ user: makeUser({ schoolId: 'S', roles }) });
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(DashboardPageComponent);
  fixture.detectChanges();
  // the School Admin and Principal dashboards carry the Pending promotion tile, which asks for its own count (it has its own spec): answer it so these tests stay about the page
  http.match((r) => r.url === `${environment.apiUrl}/api/Promotion/pending`).forEach((r) => r.flush({ status: true, statusCode: 200, message: 'ok', data: { hasEarlierYear: false, total: 0, blocked: 0, rows: [] } }));
  return { fixture, http };
}

describe('DashboardPageComponent', () => {
  afterEach(clearSessions);

  it('gives a Student their own dashboard (one StudentDashboard request)', () => {
    const { fixture, http } = open(['Student']);
    http.expectOne(`${environment.apiUrl}/api/StudentDashboard`);
    expect(fixture.nativeElement.querySelector('app-student-dashboard')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-placeholder-page')).toBeNull();
    http.verify();
  });

  it('gives a Teacher their own dashboard (one TeacherDashboard request, no Student request)', () => {
    const { fixture, http } = open(['Teacher']);
    http.expectOne(`${environment.apiUrl}/api/TeacherDashboard`);
    expect(fixture.nativeElement.querySelector('app-teacher-dashboard')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-student-dashboard')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-placeholder-page')).toBeNull();
    http.verify();
  });

  it('gives the Super Admin their own dashboard (one dashboard summary request plus the enquiry tiles own request: the tables live on /subscriptions and /invoices)', () => {
    const { fixture, http } = open(['Super Admin']);
    http.expectOne(`${environment.apiUrl}/api/SuperAdminDashboard/summary`);
    http.expectOne(`${environment.apiUrl}/api/superadmin/enquiries/summary`); // the enquiry tiles at the top
    expect(fixture.nativeElement.querySelector('app-super-admin-dashboard')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-placeholder-page')).toBeNull();
    http.verify();
  });

  it('gives a School Admin their own dashboard (one Dashboard/school-admin request plus the year list for its selector, no placeholder)', () => {
    const { fixture, http } = open(['School Admin']);
    http.expectOne(`${environment.apiUrl}/api/Dashboard/school-admin`);
    http.expectOne(`${environment.apiUrl}/api/AcademicYear`);   // the dashboard's year selector
    expect(fixture.nativeElement.querySelector('app-school-admin-dashboard')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-placeholder-page')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-super-admin-dashboard')).toBeNull();
    http.verify();
  });

  it('keeps the Teacher dashboard for an account that is both a Teacher and a School Admin (existing behaviour unchanged)', () => {
    const { fixture, http } = open(['Teacher', 'School Admin']);
    http.expectOne(`${environment.apiUrl}/api/TeacherDashboard`);
    expect(fixture.nativeElement.querySelector('app-school-admin-dashboard')).toBeNull();
    http.verify();
  });

  it('gives the Principal their own dashboard (one PrincipalDashboard request, no placeholder)', () => {
    const { fixture, http } = open(['Principal']);
    http.expectOne(`${environment.apiUrl}/api/PrincipalDashboard/summary`);
    http.expectOne(`${environment.apiUrl}/api/AcademicYear`);   // the dashboard's year selector
    expect(fixture.nativeElement.querySelector('app-principal-dashboard')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-placeholder-page')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-school-admin-dashboard')).toBeNull();
    http.verify();
  });

  it('keeps the School Admin dashboard for an account that is both a School Admin and a Principal', () => {
    const { fixture, http } = open(['School Admin', 'Principal']);
    http.expectOne(`${environment.apiUrl}/api/Dashboard/school-admin`);
    http.expectOne(`${environment.apiUrl}/api/AcademicYear`);   // the dashboard's year selector
    expect(fixture.nativeElement.querySelector('app-principal-dashboard')).toBeNull();
    http.verify();
  });

  it('gives the Accountant their own dashboard (one AccountantDashboard request, no placeholder)', () => {
    const { fixture, http } = open(['Accountant']);
    http.expectOne(`${environment.apiUrl}/api/AccountantDashboard/summary`);
    http.expectOne(`${environment.apiUrl}/api/AcademicYear`);   // the dashboard's year selector
    expect(fixture.nativeElement.querySelector('app-accountant-dashboard')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-placeholder-page')).toBeNull();
    http.verify();
  });

  it('gives Admission Staff their own dashboard (one AdmissionDashboard request, no placeholder)', () => {
    const { fixture, http } = open(['Admission Staff']);
    http.expectOne(`${environment.apiUrl}/api/AdmissionDashboard/summary`);
    expect(fixture.nativeElement.querySelector('app-admission-dashboard')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-placeholder-page')).toBeNull();
    http.verify();
  });

  it('keeps the Principal dashboard for an account that is both a Principal and Admission Staff', () => {
    const { fixture, http } = open(['Principal', 'Admission Staff']);
    http.expectOne(`${environment.apiUrl}/api/PrincipalDashboard/summary`);
    http.expectOne(`${environment.apiUrl}/api/AcademicYear`);   // the dashboard's year selector
    expect(fixture.nativeElement.querySelector('app-admission-dashboard')).toBeNull();
    http.verify();
  });

  it.each([['Test Role']])('leaves a custom role (%s) on the existing placeholder and calls nothing (a Guardian is sent to /family instead)', (role) => {
    const { fixture, http } = open([role]);
    expect(fixture.nativeElement.querySelector('app-placeholder-page')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-student-dashboard')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-teacher-dashboard')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-school-admin-dashboard')).toBeNull();
    http.verify(); // no request was made
  });

  describe('Store Keeper landing (Inventory)', () => {
    const spyNavigate = () => vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    it('a Store Keeper is sent to /inventory, without a history entry, and makes no dashboard request', () => {
      clearSessions();
      seedSession({ user: makeUser({ schoolId: 'S', roles: ['Store Keeper'] }) });
      TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
      const navigate = spyNavigate();
      const http = TestBed.inject(HttpTestingController);
      const fixture = TestBed.createComponent(DashboardPageComponent);
      fixture.detectChanges();
      expect(navigate).toHaveBeenCalledWith('/inventory', { replaceUrl: true });
      expect(fixture.nativeElement.querySelector('app-placeholder-page')).toBeNull();
      http.verify();
    });

    it.each([
      ['Student'], ['Super Admin'], ['Teacher'], ['School Admin'], ['Principal'], ['Accountant'], ['Admission Staff'],
    ])('%s still lands exactly where it did: no redirect to /inventory', (role) => {
      clearSessions();
      seedSession({ user: makeUser({ schoolId: 'S', roles: [role] }) });
      TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
      const navigate = spyNavigate();
      const fixture = TestBed.createComponent(DashboardPageComponent);
      fixture.detectChanges();
      expect(navigate).not.toHaveBeenCalled();
    });

    it('a Guardian has no dashboard: they are sent to the family screen (and never to /inventory)', () => {
      clearSessions();
      seedSession({ user: makeUser({ schoolId: 'S', roles: ['Guardian'] }) });
      TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
      const navigate = spyNavigate();
      const fixture = TestBed.createComponent(DashboardPageComponent);
      fixture.detectChanges();
      expect(navigate).toHaveBeenCalledTimes(1);
      expect(navigate.mock.calls[0][0]).toBe('/family');
    });

    it('an account that is a Store Keeper AND holds another dashboard role keeps the other role dashboard', () => {
      for (const other of ['Teacher', 'School Admin', 'Principal', 'Accountant', 'Admission Staff', 'Student']) {
        TestBed.resetTestingModule();
        clearSessions();
        seedSession({ user: makeUser({ schoolId: 'S', roles: ['Store Keeper', other] }) });
        TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
        const navigate = spyNavigate();
        TestBed.createComponent(DashboardPageComponent).detectChanges();
        expect([other, navigate.mock.calls.length]).toEqual([other, 0]);
      }
    });
  });
});
