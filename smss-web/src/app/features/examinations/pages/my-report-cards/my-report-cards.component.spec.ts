import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { MyReportCard, MyReportCardDetail } from '../../models/results.model';
import { MyReportCardsComponent } from './my-report-cards.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const card = (over: Partial<MyReportCard> = {}): MyReportCard => ({ publicationId: 7, schemeName: 'Class 3 scheme', className: 'Class 3', sectionName: 'A', scope: 'TERM', termName: 'Term I', version: 2, publishedAt: '2026-11-06T10:00:00Z', result: 'PASS', percent: 74.38, grade: 'B', rank: 5, ...over });
const DETAIL: MyReportCardDetail = {
  card: card(),
  model: {
    sessionName: '2026-27', mode: 'MARKS', showRank: true, terms: [{ termId: 5, name: 'Term I' }],
    subjects: [{ name: 'English', terms: [{ termId: 5, obtained: 80, outOf: 100, grade: 'A', failed: false }] }],
    termSummaries: [], overall: { obtained: 80, outOf: 100, percent: 80, grade: 'A', result: 'PASS', rank: 1, rankedCount: 10 }, promotion: { decision: 'PROMOTED', promotedTo: 'Class 4', newSessionDate: '01 Apr 2027' },
  },
};

describe('MyReportCardsComponent', () => {
  let fixture: ComponentFixture<MyReportCardsComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const button = (label: string) => [...el().querySelectorAll('button')].find((b) => b.textContent?.includes(label)) as HTMLButtonElement | undefined;

  function open(cards: MyReportCard[]) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Student', roles: ['Student'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MyReportCardsComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Student/me/report-cards`).flush(env(cards));
    fixture.detectChanges();
  }
  afterEach(() => { http.verify(); clearSessions(); vi.restoreAllMocks(); });

  it('says so plainly when nothing has been published', () => {
    open([]);
    expect(text()).toContain('No report card has been published for you yet');
  });

  it('lists each published card with its result, percentage, grade, rank and version', () => {
    open([card(), card({ publicationId: 8, scope: 'YEAR', termName: null, version: 1, result: 'FAIL', percent: 30, grade: 'E', rank: null })]);
    expect(text()).toContain('Term I');
    expect(text()).toContain('Whole year');
    expect(text()).toContain('Passed');
    expect(text()).toContain('74.38%');
    expect(text()).toContain('Rank 5');
    expect(text()).toContain('Failed');
    expect(text()).toContain('version 2');
  });

  it('View summary loads the stored card for that publication and shows the subject totals and the promotion', () => {
    open([card()]);
    button('View summary')!.click(); fixture.detectChanges();
    http.expectOne(`${API}/api/Student/me/report-cards/7`).flush(env(DETAIL));
    fixture.detectChanges();
    expect(text()).toContain('English');
    expect(text()).toContain('80/100 · A');
    expect(text()).toContain('Promotion: Promoted to Class 4');
    expect(text()).toContain('new session begins 01 Apr 2027');
    expect(button('Hide summary')).toBeTruthy();
  });

  it('Download PDF asks only for this student\'s publication', () => {
    open([card()]);
    button('Download PDF')!.click();
    const req = http.expectOne(`${API}/api/Student/me/report-cards/7/pdf`);
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['%PDF'], { type: 'application/pdf' }));
  });

  it('a load failure offers Try again', () => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Student', roles: ['Student'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MyReportCardsComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Student/me/report-cards`).flush({ message: 'x' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(text()).toContain('could not be loaded');
    expect(button('Try again')).toBeTruthy();
  });
});
