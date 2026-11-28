import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { YearContextService } from '../../../core/services/year-context.service';
import { clearSessions, makeUser, seedSession } from '../../../core/testing/auth-test-utils';
import { ViewingYearBannerComponent } from './viewing-year-banner.component';

const yr = (id: number, name: string, isCurrent: boolean) => ({ academicYearId: id, schoolId: 'S', yearName: name, startDate: '2026-04-01', endDate: '2027-03-31', isCurrent, statusName: 'Active', state: isCurrent ? 'CURRENT' : 'PAST', isClosed: false, usage: {}, createdAt: '', updatedAt: '' });
const YEARS = [yr(1, '2025-26', false), yr(2, '2026-27', true)];

describe('ViewingYearBannerComponent', () => {
  let http: HttpTestingController;

  function open(roles: string[]) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles }) });
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ViewingYearBannerComponent);
    fixture.detectChanges();
    http.match(`${environment.apiUrl}/api/AcademicYear`).forEach((r) => r.flush({ status: true, statusCode: 200, message: 'ok', data: YEARS }));
    TestBed.tick();
    fixture.detectChanges();
    return fixture;
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('says nothing while the current year is being viewed', () => {
    const f = open(['School Admin']);
    expect(f.nativeElement.querySelector('[data-viewing-year-banner]')).toBeNull();
  });

  it('says in words that the page works on the CURRENT year when another year is being viewed, and can switch back', () => {
    const f = open(['School Admin']);
    TestBed.inject(YearContextService).select('1');
    f.detectChanges();
    const text = (f.nativeElement.querySelector('[data-viewing-year-banner]')?.textContent ?? '').replace(/\s+/g, ' ');
    expect(text).toContain('2025-26');
    expect(text).toContain('always works on the current year 2026-27');
    (f.nativeElement.querySelector('[data-viewing-year-back]') as HTMLButtonElement).click();
    f.detectChanges();
    expect(f.nativeElement.querySelector('[data-viewing-year-banner]')).toBeNull();
  });

  it('never shows for a Teacher (no selector, no viewing year)', () => {
    const f = open(['Teacher']);
    expect(f.nativeElement.querySelector('[data-viewing-year-banner]')).toBeNull();
  });
});
