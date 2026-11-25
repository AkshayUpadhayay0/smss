import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { canAccess } from '../../../../core/config/route-roles';
import { PendingPromotionComponent } from './pending-promotion.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const ROW = (id: string, status: string, out = 0) => ({ studentId: id, admissionNumber: 'A' + id, studentName: 'Stu ' + id, classId: 1, className: 'Class 1', sectionId: 2, sectionName: 'A', rollNumber: '1', teacherDecision: null, outstanding: out, status });

describe('PendingPromotionComponent', () => {
  let fixture: ComponentFixture<PendingPromotionComponent>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;
  function open(answer: (r: import('@angular/common/http/testing').TestRequest) => void) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PendingPromotionComponent);
    fixture.detectChanges();
    answer(http.expectOne((r) => r.url === `${API}/api/Promotion/pending`));
    fixture.detectChanges();
  }
  afterEach(() => http.verify());

  it('lists the waiting students with BLOCKED or no decision yet', () => {
    open((r) => r.flush(env({ hasEarlierYear: true, currentYearName: '2027-28', earlierYearName: '2026-27', total: 2, blocked: 1, rows: [ROW('S1', 'BLOCKED', 500), ROW('S2', 'NO_DECISION')] })));
    expect(root().querySelector('[data-summary]')?.textContent).toContain('2 students');
    expect(root().querySelector('[data-row="S1"]')?.textContent).toContain('Blocked');
    expect(root().querySelector('[data-row="S2"]')?.textContent).toContain('No decision yet');
  });

  it('says so when there is no earlier year or nobody waits, and shows a calm message on failure', () => {
    open((r) => r.flush(env({ hasEarlierYear: false, currentYearName: 'x', earlierYearName: null, total: 0, blocked: 0, rows: [] })));
    expect(root().querySelector('[data-no-earlier]')).not.toBeNull();
  });

  it('a failed load shows a message', () => {
    open((r) => r.flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' }));
    expect(root().querySelector('[data-failed]')).not.toBeNull();
  });

  it('only the School Admin and the Principal open it', () => {
    expect(canAccess('/promotion/pending', ['School Admin'])).toBe(true);
    expect(canAccess('/promotion/pending', ['Principal'])).toBe(true);
    for (const r of ['Teacher', 'Accountant', 'Admission Staff', 'Student', 'Guardian', 'Super Admin']) expect(canAccess('/promotion/pending', [r])).toBe(false);
  });
});
