import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { YearContextService } from '../../../../core/services/year-context.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const YEARS = [
  { academicYearId: 2, yearName: '2025-26', isCurrent: false, statusName: 'Active' },
  { academicYearId: 3, yearName: '2026-27', isCurrent: true, statusName: 'Active' },
];
const CLASSES = [{ classId: 7, className: 'Class 1', statusName: 'Active' }];
const SECTIONS = [{ sectionId: 20, classId: 7, className: 'Class 1', sectionName: 'A', statusName: 'Active' }];
import { FeeDashboardComponent } from './fee-dashboard.component';

const DASH = (canManage: boolean) => ({
  academicYearId: 3, academicYearName: '2026-27', classId: null, sectionId: null, upcomingDays: 7, asOf: '2026-10-17',
  totalDue: 136000, totalCollected: 10200, concessionGiven: 1500, totalOutstanding: 125800, overdueAmount: 49800, overdueCount: 19, upcomingAmount: 3000, upcomingCount: 2,
  concessionByType: [
    { concessionId: 1, concessionName: 'Sibling 10%', concessionCode: 'SIB10', concessionAmount: 1000, paymentCount: 3 },
    { concessionId: 2, concessionName: 'Staff Rs 500', concessionCode: 'STF', concessionAmount: 500, paymentCount: 1 },
  ],
  collectionByMonth: [{ month: '2026-09-01', label: 'Sep 2026', amount: 0 }, { month: '2026-10-01', label: 'Oct 2026', amount: 10200 }],
  canManage,
});

describe('FeeDashboardComponent', () => {
  let fixture: ComponentFixture<FeeDashboardComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;
  const dashReqs = () => http.match((r) => r.url === `${API}/api/FeeDashboard`);

  function open(roles: string[], canManage: boolean) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: new Map() } } }] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(FeeDashboardComponent);
    fixture.detectChanges();
    http.match(`${API}/api/AcademicYear`).forEach((r) => r.flush(env(YEARS)));   // the header's viewing year and this page's own option list both ask
    TestBed.tick();
    http.expectOne(`${API}/api/Classes`).flush(env(CLASSES));
    http.expectOne(`${API}/api/Sections`).flush(env(SECTIONS));
    const [first] = dashReqs();
    first.flush(env(DASH(canManage)));
    fixture.detectChanges();
    return first;
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('loads the CURRENT year with a 7 day window and shows the four cards and the month table', () => {
    const req = open(['School Admin'], true);
    expect(req.request.params.get('academicYearId')).toBe('3');
    expect(req.request.params.get('days')).toBe('7');
    expect(req.request.params.has('classId')).toBe(false);
    const text = el().textContent ?? '';
    for (const t of ['Collected', 'Outstanding', 'Overdue', 'Upcoming', '₹10,200.00', '₹1,25,800.00', '₹49,800.00', '19 installments past due', '2 installments due in the next 7 days', 'Oct 2026']) expect(text).toContain(t);
  });

  it('class / section / window changes reload from the server with the right params (a new class clears the section)', () => {
    open(['School Admin'], true);
    cmp()['classControl'].setValue('7');
    let [r] = dashReqs();
    expect(r.request.params.get('classId')).toBe('7');
    r.flush(env(DASH(true)));
    cmp()['sectionControl'].setValue('20');
    [r] = dashReqs();
    expect(r.request.params.get('sectionId')).toBe('20');
    r.flush(env(DASH(true)));
    cmp()['daysControl'].setValue('30');
    [r] = dashReqs();
    expect(r.request.params.get('days')).toBe('30');
    r.flush(env(DASH(true)));
    cmp()['classControl'].setValue('');
    [r] = dashReqs();
    expect(r.request.params.has('classId')).toBe(false);
    expect(r.request.params.has('sectionId')).toBe(false);
    r.flush(env(DASH(true)));
  });

  it('School Admin / Accountant see the Class-wise report button', () => {
    open(['Accountant'], true);
    expect(el().textContent).toContain('Class-wise report');
    expect(el().textContent).not.toContain('view-only');
  });

  it('Principal sees the same numbers but NO drill-down, only a view-only note', () => {
    open(['Principal'], false);
    expect(el().textContent).toContain('₹49,800.00');
    expect(el().textContent).not.toContain('Class-wise report');
    expect(el().textContent).toContain('view-only access');
  });

  it('shows a "Concessions given" card with the total and the per-concession breakdown, read-only, with a link to the full list', () => {
    open(['Principal'], false);
    const card = [...el().querySelectorAll('app-card')].find((c) => c.textContent?.includes('Concessions given'))!;
    const text = (card.textContent ?? '').replace(/\s+/g, ' ');
    expect(text).toContain('₹1,500.00');
    expect(text).toContain('Sibling 10%');
    expect(text).toContain('₹1,000.00');
    expect(text).toContain('Staff Rs 500');
    expect(card.querySelector('a[href="/fee-reports/concessions-given"]')).not.toBeNull();
    expect(card.querySelector('button')).toBeNull();                       // nothing to click: read-only
    expect(text).toContain('not part of "Collected"'.replace('not', 'Not'));
  });

  it('has no year picker of its own and follows the header viewing year (an explicit id on every request)', () => {
    open(['School Admin'], true);
    expect(el().querySelector('app-select[label="Academic year"]')).toBeNull();
    expect(el().querySelector('[data-year-note]')?.textContent).toContain('2026-27');
    TestBed.inject(YearContextService).select('2');
    TestBed.tick();
    const [r] = dashReqs();
    expect(r.request.params.get('academicYearId')).toBe('2');
    r.flush(env(DASH(true)));
  });
});
