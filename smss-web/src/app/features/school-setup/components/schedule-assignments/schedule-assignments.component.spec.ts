import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { YearSchedules } from '../../models/academic-setup.model';
import { ScheduleAssignmentsComponent, duplicateKeys, toAssignmentDrafts, toAssignmentRequest } from './schedule-assignments.component';

const API = environment.apiUrl;
const envelope = (data: unknown) => ({ status: true, statusCode: 200, message: 'ok', data });

function year(over: Partial<YearSchedules> = {}): YearSchedules {
  return {
    academicYearId: 7, yearName: '2026-27', isClosed: false, defaultScheduleId: 5,
    schedules: [{ scheduleId: 5, scheduleName: 'Regular', isDefault: true }, { scheduleId: 6, scheduleName: 'Saturday', isDefault: false }],
    classes: [{ classId: 1, className: 'Class 1' }, { classId: 2, className: 'Class 2' }],
    rows: [], ...over,
  };
}

describe('assignment helpers', () => {
  it('maps rows to drafts and back, an empty class or day meaning every', () => {
    const y = year({ rows: [{ classId: null, dayOfWeek: 6, scheduleId: 6 }, { classId: 2, dayOfWeek: null, scheduleId: 6 }] });
    const d = toAssignmentDrafts(y);
    expect(d).toEqual([{ classId: '', day: '6', scheduleId: '6' }, { classId: '2', day: '', scheduleId: '6' }]);
    expect(toAssignmentRequest(d)).toEqual({ rows: [{ classId: null, dayOfWeek: 6, scheduleId: 6 }, { classId: 2, dayOfWeek: null, scheduleId: 6 }] });
  });
  it('finds the same class and day twice', () => {
    expect([...duplicateKeys([{ classId: '', day: '6', scheduleId: '6' }, { classId: '', day: '6', scheduleId: '5' }, { classId: '1', day: '6', scheduleId: '6' }])]).toEqual(['|6']);
  });
});

describe('ScheduleAssignmentsComponent', () => {
  let fixture: ComponentFixture<ScheduleAssignmentsComponent>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;
  const button = (label: string) => Array.from(root().querySelectorAll('app-button')).find((b) => b.textContent?.trim() === label)!.querySelector('button') as HTMLButtonElement;
  const URL = `${API}/api/AcademicSetup/years/7/schedules`;

  async function open(y: YearSchedules, readOnly = false) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ScheduleAssignmentsComponent);
    fixture.componentRef.setInput('yearId', 7);
    fixture.componentRef.setInput('readOnly', readOnly);
    fixture.detectChanges();
    await fixture.whenStable();
    http.expectOne(URL).flush(envelope(y));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('says every class follows the default when there is nothing assigned', async () => {
    await open(year());
    expect(root().textContent).toContain('Every class follows');
    expect(root().textContent).toContain('Regular');
    expect(button('Save').disabled).toBe(true);
  });

  it('adding a line "every class, Saturday" and saving sends the whole set', async () => {
    await open(year());
    button('Add a line').click();
    fixture.detectChanges();
    const day = root().querySelector('[data-row="0"] [data-day]') as HTMLSelectElement;
    day.value = '6';
    day.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(button('Save').disabled).toBe(false);
    button('Save').click();
    const req = http.expectOne(URL);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ rows: [{ classId: null, dayOfWeek: 6, scheduleId: 6 }] });
    req.flush(envelope(year({ rows: [{ classId: null, className: null, dayOfWeek: 6, scheduleId: 6, scheduleName: 'Saturday' }] })));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(button('Save').disabled).toBe(true);
  });

  it('the same class and day twice blocks Save and says so', async () => {
    await open(year({ rows: [{ classId: null, dayOfWeek: 6, scheduleId: 6 }] }));
    button('Add a line').click();
    fixture.detectChanges();
    const day = root().querySelector('[data-row="1"] [data-day]') as HTMLSelectElement;
    day.value = '6';
    day.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(root().textContent).toContain('Same class and day twice');
    expect(button('Save').disabled).toBe(true);
  });

  it('a closed year is read-only', async () => {
    await open(year({ isClosed: true, rows: [{ classId: null, dayOfWeek: 6, scheduleId: 6 }] }));
    expect(root().textContent).toContain('is closed');
    expect((root().querySelector('[data-row="0"] [data-day]') as HTMLSelectElement).disabled).toBe(true);
    expect(button('Add a line').disabled).toBe(true);
    expect(button('Save').disabled).toBe(true);
  });

  it('with only one schedule it explains there is nothing to assign yet', async () => {
    await open(year({ schedules: [{ scheduleId: 5, scheduleName: 'Regular', isDefault: true }] }));
    expect(root().textContent).toContain('only one schedule');
  });
});
