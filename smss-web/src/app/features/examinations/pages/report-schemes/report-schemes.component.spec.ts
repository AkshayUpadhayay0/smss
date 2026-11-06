import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { ReportSchemesComponent } from './report-schemes.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
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
});
