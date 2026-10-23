import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { SchoolEvent } from '../../models/calendar.model';
import { CalendarComponent } from './calendar.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });

const CATS = [
  { eventCategoryId: 1, categoryName: 'Holiday', categoryCode: 'HOLIDAY', colorHex: '#22c55e' },
  { eventCategoryId: 2, categoryName: 'Exam', categoryCode: 'EXAM', colorHex: '#ef4444' },
];
const pad = (n: number) => String(n).padStart(2, '0');
const now = new Date();
const monthDay = (d: number) => `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(d)}`;
const ev = (id: number, title: string, start: string, end: string, extra: Partial<SchoolEvent> = {}): SchoolEvent => ({
  eventId: id, academicYearId: 1, eventTitle: title, startDate: start, endDate: end, isAllDay: true, startTime: null, endTime: null,
  eventCategoryId: 1, categoryName: 'Holiday', categoryCode: 'HOLIDAY', colorHex: '#22c55e', description: null, createdByName: 'Admin', ...extra,
});

describe('CalendarComponent', () => {
  let fixture: ComponentFixture<CalendarComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;
  const rangeReqs = () => http.match((r) => r.url === `${API}/api/SchoolEvent`);
  const button = (label: string) => [...el().querySelectorAll('button')].find((b) => b.textContent?.includes(label)) as HTMLButtonElement | undefined;

  function open(roles: string[], events: SchoolEvent[] = [], upcoming: SchoolEvent[] = []) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', roles }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CalendarComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/SchoolEvent/categories`).flush(env(CATS));
    const [range] = rangeReqs();
    range.flush(env(events));
    http.expectOne((r) => r.url === `${API}/api/SchoolEvent/upcoming`).flush(env(upcoming));
    fixture.detectChanges();
    return range;
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('opens on the current month with the whole 6-week grid requested (so neighbouring days have their events too)', () => {
    const range = open(['Teacher']);
    const weeks = cmp()['weeks']();
    expect(range.request.params.get('from')).toBe(weeks[0][0].date);
    expect(range.request.params.get('to')).toBe(weeks[5][6].date);
    expect(el().querySelector('.month')?.textContent).toContain(String(now.getFullYear()));
    expect(el().querySelectorAll('[role="gridcell"]')).toHaveLength(42);
    expect(el().querySelector('.day.today')).not.toBeNull();
  });

  it('draws events as coloured chips on the days they cover, a multi-day event on each day', () => {
    open(['Teacher'], [ev(1, 'Diwali', monthDay(10), monthDay(12)), ev(2, 'Maths exam', monthDay(10), monthDay(10), { isAllDay: false, startTime: '09:30:00', endTime: '11:30:00', colorHex: '#ef4444', categoryName: 'Exam' })]);
    const chips = [...el().querySelectorAll<HTMLElement>('.event')];
    expect(chips.filter((c) => c.textContent?.includes('Diwali'))).toHaveLength(3);                   // 10th, 11th, 12th
    expect(chips.some((c) => c.textContent?.includes('9:30 AM Maths exam'))).toBe(true);
    expect(chips.find((c) => c.textContent?.includes('Maths'))?.style.getPropertyValue('--c')).toBe('#ef4444');
  });

  it('a day with more than 3 events shows "+N more"; selecting the day lists them all below', () => {
    const many = [1, 2, 3, 4, 5].map((i) => ev(i, `Event ${i}`, monthDay(15), monthDay(15)));
    open(['Teacher'], many);
    expect([...el().querySelectorAll('.more')].map((m) => m.textContent?.trim())).toContain('+2 more');
    const cell = [...el().querySelectorAll<HTMLElement>('.day')].find((d) => d.getAttribute('aria-label')?.startsWith(monthDay(15)))!;
    cell.click();
    fixture.detectChanges();
    expect(el().querySelectorAll('.list .row')).toHaveLength(5);
  });

  it('next / previous month fetch that month\'s grid; Today comes back', () => {
    open(['Teacher']);
    const first = cmp()['weeks']()[0][0].date;
    button('Today')!.click();
    (el().querySelector('[aria-label="Next month"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const reqs = rangeReqs();
    expect(reqs.length).toBeGreaterThan(0);
    const next = reqs[reqs.length - 1];
    expect(next.request.params.get('from')).not.toBe(first);
    next.flush(env([]));
    reqs.slice(0, -1).forEach((r) => r.flush(env([])));
    (el().querySelector('[aria-label="Previous month"]') as HTMLButtonElement).click();
    const back = rangeReqs();
    expect(back[back.length - 1].request.params.get('from')).toBe(first);
    back.forEach((r) => r.flush(env([])));
  });

  it('the upcoming list shows the next events, an on-going one marked', () => {
    open(['Teacher'], [], [ev(1, 'Exam week', monthDay(1), monthDay(20), { isOngoing: true }), ev(2, 'PTM', monthDay(25), monthDay(25))]);
    const side = el().querySelector('aside')!.textContent ?? '';
    expect(side).toContain('Exam week');
    expect(side).toContain('on now');
    expect(side).toContain('PTM');
  });

  it('the legend lists the categories with their colours', () => {
    open(['Student']);
    expect([...el().querySelectorAll('.legend li')].map((l) => l.textContent?.trim())).toEqual(['Holiday', 'Exam']);
  });

  describe('read-only roles (Teacher, Student, Accountant, Guardian)', () => {
    it.each(['Teacher', 'Student', 'Accountant', 'Guardian'])('%s: no Add event button, and the event details have no Edit / Delete', (role) => {
      open([role], [ev(1, 'Diwali', monthDay(10), monthDay(10), { description: 'Festival of lights' })]);
      expect(button('Add event')).toBeUndefined();
      expect(button('Add event on this day')).toBeUndefined();
      (el().querySelector('.event') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(el().textContent).toContain('Festival of lights');
      expect(button('Edit')).toBeUndefined();
      expect(button('Delete')).toBeUndefined();
      expect(button('Close')).toBeDefined();
    });
  });

  describe('School Admin and Principal', () => {
    it.each(['School Admin', 'Principal'])('%s: can add, and sees Edit / Delete in the details', (role) => {
      open([role], [ev(1, 'Diwali', monthDay(10), monthDay(10))]);
      expect(button('Add event')).toBeDefined();
      (el().querySelector('.event') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(button('Edit')).toBeDefined();
      expect(button('Delete')).toBeDefined();
    });

    it('Add event opens the form; "Add event on this day" starts on the selected day', () => {
      open(['School Admin']);
      cmp()['select']({ date: monthDay(14), day: 14, inMonth: true, isToday: false });
      fixture.detectChanges();
      button('Add event on this day')!.click();
      fixture.detectChanges();
      expect(el().querySelector('app-event-form-modal')).not.toBeNull();
      expect(cmp()['form']().date).toBe(monthDay(14));
    });

    it('Delete asks first, then calls DELETE and refreshes the grid and the upcoming list', async () => {
      open(['School Admin'], [ev(7, 'Diwali', monthDay(10), monthDay(10))]);
      vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
      await cmp()['remove'](ev(7, 'Diwali', monthDay(10), monthDay(10)));
      const del = http.expectOne(`${API}/api/SchoolEvent/7`);
      expect(del.request.method).toBe('DELETE');
      del.flush(env(ev(7, 'Diwali', monthDay(10), monthDay(10))));
      rangeReqs().forEach((r) => r.flush(env([])));
      http.expectOne((r) => r.url === `${API}/api/SchoolEvent/upcoming`).flush(env([]));
      fixture.detectChanges();
      expect(el().querySelector('.event')).toBeNull();
    });

    it('declining the confirmation deletes nothing', async () => {
      open(['School Admin']);
      vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(false);
      await cmp()['remove'](ev(7, 'Diwali', monthDay(10), monthDay(10)));
      http.expectNone(`${API}/api/SchoolEvent/7`);
    });
  });

  it('a failed load keeps the page and offers a retry', () => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', roles: ['Teacher'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CalendarComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/SchoolEvent/categories`).flush(env(CATS));
    rangeReqs()[0].flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    http.expectOne((r) => r.url === `${API}/api/SchoolEvent/upcoming`).flush(env([]));
    fixture.detectChanges();
    expect(el().textContent).toContain('The calendar could not be loaded');
    expect(el().querySelectorAll('[role="gridcell"]')).toHaveLength(42);
  });
});
