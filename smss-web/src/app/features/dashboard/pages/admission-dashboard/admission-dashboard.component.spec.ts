import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { AdmissionDashboard } from '../../models/admission-dashboard.model';
import { AdmissionDashboardComponent } from './admission-dashboard.component';

const URL = `${environment.apiUrl}/api/AdmissionDashboard/summary`;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

const STATUSES = ['APPLICATION_SUBMITTED', 'UNDER_REVIEW', 'APPROVED', 'ADMITTED', 'REJECTED', 'CANCELLED'];

export function admissionDashboard(over: Partial<AdmissionDashboard> = {}): AdmissionDashboard {
  return {
    today: '2026-11-14',
    byStatus: STATUSES.map((s, i) => ({ statusName: s, count: [3, 3, 1, 3, 1, 1][i] })),
    totalApplications: 12, newThisWeek: 5, missingDocumentsCount: 2,
    missingDocuments: [{ admissionId: 3, applicationNumber: 'APS26-0003', applicantFullName: 'Asha Verma', statusName: 'UNDER_REVIEW', applicationDate: '2026-11-08T00:00:00', missingDocuments: ['Birth Certificate', 'Transfer Certificate'] }],
    recentApplications: [{ admissionId: 12, applicationNumber: 'APS26-0012', applicantFullName: 'Ravi Rao', appliedClassName: 'Class 1', statusName: 'APPLICATION_SUBMITTED', applicationDate: '2026-11-12T00:00:00' }],
    partsUnavailable: [],
    ...over,
  };
}

describe('AdmissionDashboardComponent', () => {
  let fixture: ComponentFixture<AdmissionDashboardComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => el().textContent ?? '';
  const card = (id: string) => el().querySelector(`[data-card="${id}"]`)!;

  function create(): void {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', roles: ['Admission Staff'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AdmissionDashboardComponent);
    fixture.detectChanges();
  }
  function open(d: AdmissionDashboard): void {
    create();
    http.expectOne(URL).flush(env(d));
    fixture.detectChanges();
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('makes one GET request and shows the headline numbers', () => {
    open(admissionDashboard());
    expect(http.match(() => true)).toHaveLength(0);
    expect(card('total').textContent).toContain('12');
    expect(card('new').textContent).toContain('5');
    expect(card('documents').textContent).toContain('2 open applications missing a required document');
  });

  it('lists all six statuses with their counts, in pipeline order', () => {
    open(admissionDashboard());
    const rows = [...el().querySelectorAll('[data-status]')];
    expect(rows.map((r) => r.getAttribute('data-status'))).toEqual(STATUSES);
    expect(rows.map((r) => r.querySelector('strong')?.textContent?.trim())).toEqual(['3', '3', '1', '3', '1', '1']);
    expect(rows[0].textContent).toContain('Submitted');
  });

  it('lists the applications missing documents, with what is missing, and the last applications', () => {
    open(admissionDashboard());
    expect(text()).toContain('Asha Verma');
    expect(text()).toContain('missing Birth Certificate, Transfer Certificate');
    expect(text()).toContain('Ravi Rao');
    expect(text()).toContain('APS26-0012');
  });

  it('links to Admissions', () => {
    open(admissionDashboard());
    expect(el().querySelector('[data-link="admissions"]')?.getAttribute('href')).toBe('/admissions');
    expect(card('total').getAttribute('href')).toBe('/admissions');
  });

  it('real zeros are written out, not left as a bare 0', () => {
    open(admissionDashboard({ newThisWeek: 0, missingDocumentsCount: 0, missingDocuments: [] }));
    expect(card('new').textContent).toContain('None this week');
    expect(card('documents').textContent).toContain('All documents in');
    expect(el().querySelector('[data-empty="documents"]')).not.toBeNull();
  });

  it('no applications at all says so in each place', () => {
    open(admissionDashboard({ byStatus: STATUSES.map((s) => ({ statusName: s, count: 0 })), totalApplications: 0, newThisWeek: 0, missingDocumentsCount: 0, missingDocuments: [], recentApplications: [] }));
    expect(el().querySelector('[data-empty="applications"]')).not.toBeNull();
    expect(el().querySelector('[data-empty="recent"]')).not.toBeNull();
    expect(el().querySelector('[data-empty="documents"]')).not.toBeNull();
  });

  it('a part the server could not load says Unavailable, never 0', () => {
    open(admissionDashboard({
      byStatus: [], totalApplications: null, newThisWeek: null, recentApplications: [], partsUnavailable: ['applications'],
    }));
    expect(card('total').textContent).toContain('Unavailable');
    expect(card('new').textContent).toContain('Unavailable');
    expect(card('total').textContent).not.toMatch(/\b0\b/);
    expect(text()).toContain('This could not be loaded.');
    expect(el().querySelector('[data-empty="applications"]')).toBeNull();   // unavailable is not "empty"
    expect(card('documents').textContent).toContain('2');                     // the documents part still shows
  });

  it('unavailable documents are not shown as "all documents in"', () => {
    open(admissionDashboard({ missingDocumentsCount: null, missingDocuments: [], partsUnavailable: ['documents'] }));
    expect(card('documents').textContent).toContain('Unavailable');
    expect(text()).not.toContain('All documents in');
    expect(el().querySelector('[data-empty="documents"]')).toBeNull();
  });

  it('a failed request offers Try again', () => {
    create();
    http.expectOne(URL).flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'err' });
    fixture.detectChanges();
    expect(text()).toContain('could not be loaded');
    expect([...el().querySelectorAll('button')].some((b) => b.textContent?.includes('Try again'))).toBe(true);
  });
});
