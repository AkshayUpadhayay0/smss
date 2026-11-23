import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { Schedule, ScheduleSlot } from '../../models/academic-setup.model';
import { ScheduleManagerComponent, emptySlotForm, formOf, slotFormError, slotName, toSlotRequest } from './schedule-manager.component';

const API = environment.apiUrl;
const envelope = (data: unknown) => ({ status: true, statusCode: 200, message: 'ok', data });

function slot(over: Partial<ScheduleSlot>): ScheduleSlot {
  return { periodId: 1, slotType: 'PERIOD', periodNumber: 1, label: null, startTime: '09:00:00', endTime: '09:45:00', slotOrder: 1, isActive: true, activeEntryCount: 0, ...over };
}
function schedule(over: Partial<Schedule> = {}): Schedule {
  return {
    scheduleId: 5, scheduleName: 'Regular', isDefault: true, assignments: 0, timetableEntries: 0, deleteBlockedReason: 'This is the default schedule.',
    slots: [slot({}), slot({ periodId: 2, slotType: 'BREAK', periodNumber: null, label: 'Lunch', startTime: '10:30:00', endTime: '11:00:00', slotOrder: 2 })],
    ...over,
  };
}

describe('slot form helpers', () => {
  it('a period needs a number 1 to 30, a break needs a label, both need an end after the start', () => {
    expect(slotFormError({ ...emptySlotForm(), startTime: '09:00', endTime: '09:45' })).toContain('number');
    expect(slotFormError({ ...emptySlotForm(), periodNumber: '31', startTime: '09:00', endTime: '09:45' })).toContain('number');
    expect(slotFormError({ ...emptySlotForm(), periodNumber: '3', startTime: '09:00', endTime: '09:45' })).toBeNull();
    expect(slotFormError({ ...emptySlotForm(), periodNumber: '3', startTime: '10:00', endTime: '09:45' })).toContain('after the start');
    expect(slotFormError({ ...emptySlotForm(), slotType: 'BREAK', startTime: '10:30', endTime: '11:00' })).toContain('label');
    expect(slotFormError({ ...emptySlotForm(), slotType: 'BREAK', label: 'Lunch', startTime: '10:30', endTime: '11:00' })).toBeNull();
    expect(slotFormError(emptySlotForm())).toContain('time');
  });
  it('builds the request: a break has no number, an empty label is null', () => {
    expect(toSlotRequest({ slotType: 'BREAK', periodNumber: '7', label: ' Lunch ', startTime: '10:30', endTime: '11:00' })).toEqual({ slotType: 'BREAK', periodNumber: null, label: 'Lunch', startTime: '10:30', endTime: '11:00' });
    expect(toSlotRequest({ slotType: 'PERIOD', periodNumber: '2', label: '', startTime: '09:45', endTime: '10:30' })).toEqual({ slotType: 'PERIOD', periodNumber: 2, label: null, startTime: '09:45', endTime: '10:30' });
  });
  it('fills the form from a slot and names slots', () => {
    expect(formOf(slot({}))).toEqual({ slotType: 'PERIOD', periodNumber: '1', label: '', startTime: '09:00', endTime: '09:45' });
    expect(slotName(slot({}))).toBe('Period 1');
    expect(slotName(slot({ periodNumber: 4, label: 'Maths lab' }))).toBe('Period 4 (Maths lab)');
    expect(slotName(slot({ slotType: 'BREAK', periodNumber: null, label: 'Lunch' }))).toBe('Lunch');
  });
});

describe('ScheduleManagerComponent', () => {
  let fixture: ComponentFixture<ScheduleManagerComponent>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;
  const button = (label: string, within: ParentNode = root()) => Array.from(within.querySelectorAll('app-button')).find((b) => b.textContent?.trim() === label)?.querySelector('button') as HTMLButtonElement;

  async function open(list: Schedule[]) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ScheduleManagerComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/PeriodSchedules`).flush(envelope(list));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('lists the schedules with their periods and breaks, the default marked', async () => {
    await open([schedule(), schedule({ scheduleId: 6, scheduleName: 'Saturday', isDefault: false, deleteBlockedReason: null, slots: [slot({ periodId: 9 })] })]);
    expect(root().querySelectorAll('[data-schedule]').length).toBe(2);
    expect(root().querySelector('[data-schedule="5"]')?.textContent).toContain('Default');
    expect(root().querySelector('[data-slot="2"]')?.textContent).toContain('Lunch');
    expect(root().querySelector('[data-slot="2"]')?.textContent).toContain('Break');
    expect(root().querySelector('[data-slot="1"]')?.textContent).toContain('09:00 – 09:45');
  });

  it('the default schedule cannot be deleted, an unused one can', async () => {
    await open([schedule(), schedule({ scheduleId: 6, scheduleName: 'Saturday', isDefault: false, deleteBlockedReason: null })]);
    expect(button('Delete', root().querySelector('[data-schedule="5"] header')!).disabled).toBe(true);
    expect(button('Delete', root().querySelector('[data-schedule="6"] header')!).disabled).toBe(false);
  });

  it('adds a break: the form validates, then posts the slot and shows the answer', async () => {
    await open([schedule()]);
    button('Add period or break').click();
    fixture.detectChanges();
    const type = root().querySelector('[data-slot-type]') as HTMLSelectElement;
    type.value = 'BREAK';
    type.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(button('Add slot').disabled).toBe(true);   // no times, no label yet
    const set = (sel: string, v: string) => { const el = root().querySelector(sel) as HTMLInputElement; el.value = v; el.dispatchEvent(new Event('input')); fixture.detectChanges(); };
    set('[data-slot-label]', 'Snacks');
    set('[data-slot-start]', '12:00');
    set('[data-slot-end]', '12:15');
    expect(button('Add slot').disabled).toBe(false);
    button('Add slot').click();
    const req = http.expectOne(`${API}/api/PeriodSchedules/5/slots`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ slotType: 'BREAK', periodNumber: null, label: 'Snacks', startTime: '12:00', endTime: '12:15' });
    req.flush(envelope(schedule({ slots: [slot({}), slot({ periodId: 3, slotType: 'BREAK', periodNumber: null, label: 'Snacks', startTime: '12:00:00', endTime: '12:15:00', slotOrder: 3 })] })));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root().querySelector('[data-slot="3"]')?.textContent).toContain('Snacks');
    expect(root().querySelector('[data-slot-form]')).toBeNull();   // closed after saving
  });

  it('a slot in use says so', async () => {
    await open([schedule({ slots: [slot({ activeEntryCount: 12 })] })]);
    expect(root().querySelector('[data-slot="1"]')?.textContent).toContain('In use');
  });

  it('creating a schedule posts the name and the copy source, then reloads', async () => {
    await open([schedule()]);
    const name = root().querySelector('[data-new-name]') as HTMLInputElement;
    name.value = 'Winter';
    name.dispatchEvent(new Event('input'));
    const copy = root().querySelector('[data-copy]') as HTMLSelectElement;
    copy.value = '5';
    copy.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    button('Create schedule').click();
    const req = http.expectOne(`${API}/api/PeriodSchedules`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ scheduleName: 'Winter', copyFromScheduleId: 5 });
    req.flush(envelope(schedule({ scheduleId: 7, scheduleName: 'Winter', isDefault: false })));
    http.expectOne(`${API}/api/PeriodSchedules`).flush(envelope([schedule(), schedule({ scheduleId: 7, scheduleName: 'Winter', isDefault: false, deleteBlockedReason: null })]));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root().querySelectorAll('[data-schedule]').length).toBe(2);
  });
});
