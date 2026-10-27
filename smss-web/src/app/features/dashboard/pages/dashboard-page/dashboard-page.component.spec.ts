import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
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

  it.each([['School Admin'], ['Teacher'], ['Principal'], ['Super Admin']])('leaves %s on the existing placeholder and calls nothing', (role) => {
    const { fixture, http } = open([role]);
    expect(fixture.nativeElement.querySelector('app-placeholder-page')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-student-dashboard')).toBeNull();
    http.verify(); // no request was made
  });
});
