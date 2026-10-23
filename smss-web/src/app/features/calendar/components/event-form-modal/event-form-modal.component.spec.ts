import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { EventCategory, SchoolEvent } from '../../models/calendar.model';
import { EventFormModalComponent } from './event-form-modal.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });
const CATS: EventCategory[] = [
  { eventCategoryId: 1, categoryName: 'Holiday', categoryCode: 'HOLIDAY', colorHex: '#22c55e' },
  { eventCategoryId: 2, categoryName: 'Exam', categoryCode: 'EXAM', colorHex: '#ef4444' },
];
const EXISTING: SchoolEvent = {
  eventId: 9, academicYearId: 1, eventTitle: 'Maths exam', startDate: '2026-11-03', endDate: '2026-11-03', isAllDay: false, startTime: '09:30:00', endTime: '11:30:00',
  eventCategoryId: 2, categoryName: 'Exam', categoryCode: 'EXAM', colorHex: '#ef4444', description: 'Bring a calculator',
};

describe('EventFormModalComponent', () => {
  let fixture: ComponentFixture<EventFormModalComponent>;
  let http: HttpTestingController;
  let saved: SchoolEvent[];
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;
  const form = () => cmp()['form'];

  function open(event: SchoolEvent | null = null, date = '2026-11-10') {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(EventFormModalComponent);
    fixture.componentRef.setInput('event', event);
    fixture.componentRef.setInput('initialDate', date);
    fixture.componentRef.setInput('categories', CATS);
    saved = [];
    fixture.componentInstance.saved.subscribe((e) => saved.push(e));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('add: starts all-day on the chosen day, end date = start, the first category selected', () => {
    open();
    expect(form().getRawValue()).toMatchObject({ isAllDay: true, startDate: '2026-11-10', endDate: '2026-11-10', eventCategoryId: 1 });
    expect(el().textContent).toContain('Add event');
    expect(el().querySelector('input[type="time"]')).toBeNull();            // no time fields for an all-day event
  });

  it('ticking off all-day reveals the two time fields', () => {
    open();
    form().controls.isAllDay.setValue(false);
    fixture.detectChanges();
    expect(el().querySelectorAll('input[type="time"]')).toHaveLength(2);
  });

  it('the category picker shows each category with its colour and selecting one changes the choice', () => {
    open();
    const chips = [...el().querySelectorAll<HTMLButtonElement>('.chip')];
    expect(chips.map((c) => c.textContent?.trim())).toEqual(['Holiday', 'Exam']);
    expect(chips.map((c) => (c.querySelector('.dot') as HTMLElement).style.background)).toEqual(['rgb(34, 197, 94)', 'rgb(239, 68, 68)']);
    chips[1].click();
    fixture.detectChanges();
    expect(form().controls.eventCategoryId.value).toBe(2);
    expect(chips[1].getAttribute('aria-checked')).toBe('true');
  });

  it('the end date follows the start while blank or earlier, but not when it is already later', () => {
    open();
    form().controls.startDate.setValue('2026-11-12');
    expect(form().controls.endDate.value).toBe('2026-11-12');
    form().controls.endDate.setValue('2026-11-20');
    form().controls.startDate.setValue('2026-11-13');
    expect(form().controls.endDate.value).toBe('2026-11-20');
  });

  it('will not submit an empty title, an end before the start, or a timed event without both times (nothing is sent)', () => {
    open();
    form().controls.eventTitle.setValue('   ');
    cmp()['submit']();
    http.expectNone(`${API}/api/SchoolEvent`);
    expect(cmp()['err']('eventTitle')).toBeTruthy();

    form().patchValue({ eventTitle: 'Camp', endDate: '2026-11-09' });
    cmp()['submit']();
    http.expectNone(`${API}/api/SchoolEvent`);
    expect(cmp()['err']('endDate')).toContain('cannot be before');

    form().patchValue({ endDate: '2026-11-10', isAllDay: false, startTime: '09:00', endTime: '' });
    cmp()['submit']();
    http.expectNone(`${API}/api/SchoolEvent`);
    expect(cmp()['err']('endTime')).toBeTruthy();

    form().patchValue({ startTime: '10:00', endTime: '09:00' });      // single day, ends before it starts
    cmp()['submit']();
    http.expectNone(`${API}/api/SchoolEvent`);
  });

  it('add posts the event (all-day: no times; blank description null; end date filled) and reports it saved', () => {
    open();
    form().patchValue({ eventTitle: '  Diwali  ', description: '  ' });
    cmp()['submit']();
    const req = http.expectOne(`${API}/api/SchoolEvent`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ eventTitle: 'Diwali', startDate: '2026-11-10', endDate: '2026-11-10', isAllDay: true, startTime: null, endTime: null, eventCategoryId: 1, description: null });
    req.flush(env({ ...EXISTING, eventId: 10 }, 201));
    expect(saved).toHaveLength(1);
  });

  it('a timed multi-day event may end earlier on the clock (18:00 -> 09:00 next day)', () => {
    open();
    form().patchValue({ eventTitle: 'Camp', isAllDay: false, startTime: '18:00', endDate: '2026-11-11', endTime: '09:00' });
    cmp()['submit']();
    const req = http.expectOne(`${API}/api/SchoolEvent`);
    expect(req.request.body).toMatchObject({ isAllDay: false, startTime: '18:00', endTime: '09:00', endDate: '2026-11-11' });
    req.flush(env(EXISTING, 201));
  });

  it('edit: pre-fills everything and PUTs the changes to that event', () => {
    open(EXISTING);
    expect(form().getRawValue()).toMatchObject({ eventTitle: 'Maths exam', isAllDay: false, startTime: '09:30', endTime: '11:30', eventCategoryId: 2, description: 'Bring a calculator' });
    expect(el().textContent).toContain('Edit event');
    form().controls.eventTitle.setValue('Maths exam (Paper 1)');
    cmp()['submit']();
    const req = http.expectOne(`${API}/api/SchoolEvent/9`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.eventTitle).toBe('Maths exam (Paper 1)');
    req.flush(env(EXISTING));
    expect(saved).toHaveLength(1);
  });

  it('when the server refuses (e.g. the date is outside every academic year) the form stays open with what was typed', () => {
    open();
    form().patchValue({ eventTitle: 'Odd date' });
    cmp()['submit']();
    http.expectOne(`${API}/api/SchoolEvent`).flush({ status: false, statusCode: 400, message: 'No academic year covers the start date.', data: null }, { status: 400, statusText: 'Bad Request' });
    expect(saved).toHaveLength(0);
    expect(form().controls.eventTitle.value).toBe('Odd date');
    expect(cmp()['saving']()).toBe(false);
  });
});
