import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { YearContextService } from '../../../../core/services/year-context.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { ReportSchemesComponent } from './report-schemes.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const YEAR = (id: number, name: string, isCurrent: boolean) => ({ academicYearId: id, schoolId: 'S', yearName: name, startDate: '2026-04-01', endDate: '2027-03-31', isCurrent, statusName: 'Active', state: isCurrent ? 'CURRENT' : 'PAST', isClosed: false, usage: {}, createdAt: '', updatedAt: '' });
const ROW = { schemeId: 3, schemeName: 'Class 1 scheme', academicYearId: 7, academicYearName: '2026-27', evaluationMode: 'MARKS', templateKey: 'term-wise', classNames: ['Class 1'], termCount: 2, examCount: 3, subjectCount: 5, statusName: 'Active' };

describe('ReportSchemesComponent', () => {
  let fixture: ComponentFixture<ReportSchemesComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  function open(role: string, rows: unknown[]) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: [role] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ReportSchemesComponent);
    fixture.detectChanges();
    http.expectOne((r) => r.url === `${API}/api/ReportScheme`).flush(env(rows));
    http.match(`${API}/api/AcademicYear`).forEach((r) => r.flush(env([YEAR(6, '2025-26', false), YEAR(7, '2026-27', true)])));   // the header's viewing year
    TestBed.tick();
    fixture.detectChanges();
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('the School Admin sees the schemes and both create buttons', () => {
    open('School Admin', [ROW]);
    expect(text()).toContain('Class 1 scheme');
    expect(text()).toContain('New from a preset');
    expect(text()).toContain('Blank scheme');
  });

  it('the Principal reads the list but has no create buttons', () => {
    open('Principal', [ROW]);
    expect(text()).toContain('Class 1 scheme');
    expect(text()).not.toContain('New from a preset');
    expect(text()).not.toContain('Blank scheme');
  });

  it('an empty list points the School Admin at presets', () => {
    open('School Admin', []);
    expect(text()).toContain('Start from a preset');
  });

  it('shows the schemes of the header viewing year only, with no year picker of its own', () => {
    open('School Admin', [ROW, { ...ROW, schemeId: 4, schemeName: 'Old scheme', academicYearId: 6, academicYearName: '2025-26' }]);
    expect(text()).toContain('Class 1 scheme');
    expect(text()).not.toContain('Old scheme');
    expect(el().querySelector('select[aria-label="Academic year"]')).toBeNull();
    TestBed.inject(YearContextService).select('6');
    fixture.detectChanges();
    expect(text()).toContain('Old scheme');
    expect(text()).not.toContain('Class 1 scheme');
    expect(el().querySelector('[data-year-note]')?.textContent).toContain('2025-26');
  });
});
