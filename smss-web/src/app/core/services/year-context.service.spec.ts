import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AcademicYear } from '../../features/school-setup/models/academic-year.model';
import { clearSessions, makeLoginResponse, makeUser, seedSession } from '../testing/auth-test-utils';
import { AuthService } from './auth.service';
import { YearContextService, yearLabel } from './year-context.service';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const year = (id: number, name: string, extra: Partial<AcademicYear> = {}): AcademicYear => ({
  academicYearId: id, schoolId: 'S', yearName: name, startDate: '2026-04-01', endDate: '2027-03-31', isCurrent: false, statusName: 'Active', createdAt: '', updatedAt: '',
  state: 'PAST', isClosed: false, usage: {} as AcademicYear['usage'], ...extra,
});
const PAST = year(1, '2025-26', { isClosed: true, state: 'CLOSED' });
const CUR = year(2, '2026-27', { isCurrent: true, state: 'CURRENT' });
const NEXT = year(3, '2027-28', { state: 'UPCOMING' });
const KEY = 'smss.viewingYear';

describe('YearContextService', () => {
  let http: HttpTestingController;

  function start(roles: string[] = ['School Admin'], opts: { stored?: string; years?: AcademicYear[] | 'fail'; schoolId?: string | null } = {}) {
    clearSessions();
    sessionStorage.removeItem(KEY);
    if (opts.stored !== undefined) sessionStorage.setItem(KEY, opts.stored);
    seedSession({ user: makeUser({ schoolId: opts.schoolId === undefined ? 'S' : opts.schoolId, userType: 'Employee', roles }) });
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: 'login', children: [] }])] });
    http = TestBed.inject(HttpTestingController);
    const svc = TestBed.inject(YearContextService);
    TestBed.tick();
    if (opts.years !== undefined && ['School Admin', 'Principal', 'Accountant'].some((r) => roles.includes(r)) && opts.schoolId !== null) {
      const req = http.expectOne(`${API}/api/AcademicYear`);
      if (opts.years === 'fail') req.flush({ message: 'x' }, { status: 500, statusText: 'err' });
      else req.flush(env(opts.years));
      TestBed.tick();
    }
    return svc;
  }
  afterEach(() => { http.verify(); clearSessions(); sessionStorage.removeItem(KEY); });

  it('is loading until the years are known, then defaults to the current year', () => {
    const svc = start(['School Admin']);
    expect(svc.state()).toBe('loading');
    expect(svc.viewingYearId()).toBeNull();                                   // pages wait: no id yet
    http.expectOne(`${API}/api/AcademicYear`).flush(env([PAST, CUR, NEXT]));
    TestBed.tick();
    expect(svc.state()).toBe('ready');
    expect(svc.viewingYearId()).toBe(2);
    expect(svc.isNonCurrent()).toBe(false);
    expect(svc.storedChoice()).toBe('current');
  });

  it('lists every year the school has, labelled Current, Closed or Upcoming', () => {
    const svc = start(['Principal'], { years: [PAST, CUR, NEXT, year(4, '2024-25')] });
    expect(svc.options().map((o) => o.label)).toEqual(['2025-26 (Closed)', '2026-27 (Current)', '2027-28 (Upcoming)', '2024-25 (Past)']);
    expect(yearLabel(CUR)).toBe('Current');
  });

  it('an explicit pick is stored as the id; picking the current year stores the "current" marker instead', () => {
    const svc = start(['School Admin'], { years: [PAST, CUR, NEXT] });
    svc.select('1');
    expect(svc.viewingYearId()).toBe(1);
    expect(svc.isNonCurrent()).toBe(true);
    expect(sessionStorage.getItem(KEY)).toBe('1');
    svc.select('2');                                                          // that IS the current year: follow the flag, store the marker
    expect(sessionStorage.getItem(KEY)).toBe('current');
    expect(svc.isNonCurrent()).toBe(false);
    svc.select('1');
    svc.useCurrent();
    expect(sessionStorage.getItem(KEY)).toBe('current');
  });

  it('the marker follows the current flag when an admin moves it; an explicit pick does not', () => {
    const svc = start(['School Admin'], { years: [PAST, CUR, NEXT] });
    expect(svc.viewingYearId()).toBe(2);
    svc.refresh();
    http.expectOne(`${API}/api/AcademicYear`).flush(env([PAST, { ...CUR, isCurrent: false }, { ...NEXT, isCurrent: true, state: 'CURRENT' }]));
    TestBed.tick();
    expect(svc.viewingYearId()).toBe(3);                                      // followed the new current year
    svc.select('1');
    svc.refresh();
    http.expectOne(`${API}/api/AcademicYear`).flush(env([PAST, CUR, NEXT]));
    TestBed.tick();
    expect(svc.viewingYearId()).toBe(1);                                      // an explicit pick stays put
  });

  it('a stored pick of a year that no longer exists falls back to current, and an invalid stored value is ignored', () => {
    const gone = start(['School Admin'], { stored: '99', years: [PAST, CUR] });
    expect(gone.viewingYearId()).toBe(2);
    expect(sessionStorage.getItem(KEY)).toBe('current');
    const bad = start(['School Admin'], { stored: 'DROP TABLE', years: [PAST, CUR] });
    expect(bad.viewingYearId()).toBe(2);
    expect(bad.storedChoice()).toBe('current');
  });

  it('keeps the pick across a reload of the page (same session) and starts again at current at a new sign-in', () => {
    const svc = start(['School Admin'], { stored: '1', years: [PAST, CUR] });
    expect(svc.viewingYearId()).toBe(1);                                      // survived the reload: read from the session
    const auth = TestBed.inject(AuthService);
    auth.loginEpoch.update((n) => n + 1);                                     // a new sign-in
    TestBed.tick();
    expect(svc.viewingYearId()).toBe(2);
    expect(sessionStorage.getItem(KEY)).toBe('current');
  });

  it('a real login resets the choice (through AuthService.login)', () => {
    const svc = start(['School Admin'], { stored: '1', years: [PAST, CUR] });
    const auth = TestBed.inject(AuthService);
    auth.login({ loginId: 'a', password: 'b', rememberMe: false }).subscribe();
    http.expectOne(`${API}/api/Auth/login`).flush(env(makeLoginResponse({ user: makeUser({ schoolId: 'S', roles: ['School Admin'] }) })));
    TestBed.tick();
    expect(svc.storedChoice()).toBe('current');
    expect(svc.viewingYearId()).toBe(2);
  });

  it('a school with no years says so; a failed load says so and a retry works', () => {
    const none = start(['Accountant'], { years: [] });
    expect(none.state()).toBe('none');
    expect(none.viewingYearId()).toBeNull();
    const failed = start(['Accountant'], { years: 'fail' });
    expect(failed.state()).toBe('failed');
    failed.refresh();
    http.expectOne(`${API}/api/AcademicYear`).flush(env([CUR]));
    TestBed.tick();
    expect(failed.state()).toBe('ready');
  });

  it('only School Admin, Principal and Accountant get the selector: a Teacher, Student or Guardian never loads the years', () => {
    for (const role of ['Teacher', 'Student', 'Guardian', 'Admission Staff', 'Super Admin']) {
      const svc = start([role]);
      expect(svc.canChoose()).toBe(false);
      expect(svc.state()).toBe('idle');
      http.expectNone(`${API}/api/AcademicYear`);
    }
    for (const role of ['School Admin', 'Principal', 'Accountant']) {
      const svc = start([role], { years: [CUR] });
      expect(svc.canChoose()).toBe(true);
    }
  });

  it('signing out clears the years and the choice', () => {
    const svc = start(['School Admin'], { stored: '1', years: [PAST, CUR] });
    TestBed.inject(AuthService).endSession();
    TestBed.tick();
    expect(svc.viewingYearId()).toBeNull();
    expect(svc.storedChoice()).toBe('current');
  });
});
