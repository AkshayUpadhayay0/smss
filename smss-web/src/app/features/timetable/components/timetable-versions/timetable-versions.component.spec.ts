import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { TimetableSummary } from '../../models/timetable.model';
import { TimetableVersionsComponent, actionsOf, stateLabel, whenLabel } from './timetable-versions.component';

const API = environment.apiUrl;
const envelope = (data: unknown) => ({ status: true, statusCode: 200, message: 'ok', data });

function tt(over: Partial<TimetableSummary>): TimetableSummary {
  return {
    timetableId: 1, academicYearId: 7, classId: 3, sectionId: 10, timetableName: 'Term 1', scheduleId: 5, scheduleName: 'Regular', state: 'ACTIVE', isActive: true, everActive: true,
    slotCount: 12, needsTeacherCount: 0, createdAt: '2026-11-01T10:00:00Z', updatedAt: '2026-11-02T10:00:00Z', canDelete: false, ...over,
  };
}
const LIST = [
  tt({}),
  tt({ timetableId: 2, timetableName: 'Term 2 draft', state: 'DRAFT', isActive: false, everActive: false, canDelete: true, needsTeacherCount: 2 }),
  tt({ timetableId: 3, timetableName: 'Old', state: 'ARCHIVED', isActive: false, everActive: true }),
];

describe('helpers', () => {
  it('labels states and dates', () => {
    expect(stateLabel('ACTIVE')).toBe('Active');
    expect(stateLabel('DRAFT')).toBe('Draft');
    expect(whenLabel(null)).toBe('');
    expect(whenLabel('not a date')).toBe('');
    expect(whenLabel('2026-11-02T10:00:00Z')).toContain('2026');
  });
  it('allows only what makes sense for each state, and nothing in a closed year', () => {
    expect(actionsOf(LIST[0], false)).toEqual({ activate: false, archive: false, restore: false, remove: false, edit: true });   // the active one: nothing to activate, archived by activating another
    expect(actionsOf(LIST[1], false)).toEqual({ activate: true, archive: true, restore: false, remove: true, edit: true });
    expect(actionsOf(LIST[2], false)).toEqual({ activate: true, archive: false, restore: false, remove: false, edit: false });   // was active: history, re-activate only
    expect(actionsOf(tt({ state: 'ARCHIVED', isActive: false, everActive: false }), false).restore).toBe(true);
    expect(actionsOf(LIST[1], true)).toEqual({ activate: false, archive: false, restore: false, remove: false, edit: false });
  });
});

describe('TimetableVersionsComponent', () => {
  let fixture: ComponentFixture<TimetableVersionsComponent>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;
  const row = (id: number) => root().querySelector(`[data-tt="${id}"]`) as HTMLElement;
  const button = (label: string) => Array.from(root().querySelectorAll('app-button')).find((b) => b.textContent?.trim().startsWith(label))?.querySelector('button') as HTMLButtonElement;

  function open(versions: TimetableSummary[], readOnly = false) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TimetableVersionsComponent);
    fixture.componentRef.setInput('sectionId', 10);
    fixture.componentRef.setInput('yearId', 7);
    fixture.componentRef.setInput('versions', versions);
    fixture.componentRef.setInput('selectedId', 1);
    fixture.componentRef.setInput('readOnly', readOnly);
    fixture.componentRef.setInput('siblings', [{ sectionId: 11, sectionName: 'B' }, { sectionId: 12, sectionName: 'C' }]);
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('lists the timetables with their state, schedule and slot count, and flags slots without a teacher', () => {
    open(LIST);
    expect(row(1).textContent).toContain('Active');
    expect(row(1).textContent).toContain('Regular');
    expect(row(2).textContent).toContain('Draft');
    expect(row(2).textContent).toContain('2 need a teacher');
    expect(row(3).textContent).toContain('Archived');
  });

  it('shows the buttons that fit each state (a draft can be activated and deleted, the archived one only re-activated)', () => {
    open(LIST);
    expect(row(1).querySelector('[data-activate]')).toBeNull();
    expect(row(2).querySelector('[data-activate]')).not.toBeNull();
    expect(row(2).querySelector('[data-delete]')).not.toBeNull();
    expect(row(2).querySelector('[data-archive]')).not.toBeNull();
    expect(row(3).querySelector('[data-activate]')).not.toBeNull();
    expect(row(3).querySelector('[data-delete]')).toBeNull();
    expect(row(3).querySelector('[data-restore]')).toBeNull();
  });

  it('a closed year hides every change but keeps Check', () => {
    open(LIST, true);
    expect(root().textContent).toContain('is closed');
    expect(root().querySelector('[data-new]')).toBeNull();
    expect(root().querySelector('[data-activate]')).toBeNull();
    expect(row(2).textContent).toContain('Check');
  });

  it('a new timetable is a draft: name it, optionally copy another, and the request says so', () => {
    open(LIST);
    (root().querySelector('[data-new]') as HTMLElement).click();
    fixture.detectChanges();
    const name = root().querySelector('[data-name]') as HTMLInputElement;
    name.value = 'Exam week';
    name.dispatchEvent(new Event('input'));
    const from = root().querySelector('[data-clone-from]') as HTMLSelectElement;
    from.value = '1';
    from.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    button('Create draft').click();
    const req = http.expectOne(`${API}/api/Timetables`);
    expect(req.request.body).toEqual({ sectionId: 10, academicYearId: 7, timetableName: 'Exam week', cloneFromTimetableId: 1 });
    req.flush(envelope(tt({ timetableId: 9, timetableName: 'Exam week', state: 'DRAFT', isActive: false })));
  });

  it('activating asks first, mentioning the slots without a teacher, then posts', async () => {
    open(LIST);
    const confirm = TestBed.inject(ConfirmDialogService);
    let asked = '';
    vi.spyOn(confirm, 'confirm').mockImplementation(async (o) => { asked = o.message; return true; });
    (row(2).querySelector('[data-activate]') as HTMLElement).click();
    await fixture.whenStable();
    expect(asked).toContain('"Term 1" stops being the active timetable');
    expect(asked).toContain('2 slots have no teacher');
    http.expectOne(`${API}/api/Timetables/2/activate`).flush(envelope({ timetable: LIST[1], slotsWithoutTeacher: 2 }));
  });

  it('cancelling the confirm activates nothing', async () => {
    open(LIST);
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(false);
    (row(2).querySelector('[data-activate]') as HTMLElement).click();
    await fixture.whenStable();
    http.expectNone((r) => r.url.includes('/activate'));
  });

  it('Check lists every problem and the warnings, and says nothing was saved', () => {
    open(LIST);
    (Array.from(row(2).querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Check') as HTMLElement).click();
    fixture.detectChanges();
    http.expectOne(`${API}/api/Timetables/2/validate`).flush(envelope({
      timetableId: 2, timetableName: 'Term 2 draft', canActivate: false, slotCount: 3, slotsWithoutTeacher: 1,
      errors: [{ severity: 'ERROR', code: 'TEACHER_CLASH', message: 'Asha Rao is already teaching Class 1 - A (Maths) on Monday, period 1.' }, { severity: 'ERROR', code: 'BREAK', message: 'Lunch is a break.' }],
      warnings: [{ severity: 'WARNING', code: 'NEEDS_TEACHER', message: 'Art on Tuesday, period 2 has no teacher yet.' }],
    }));
    fixture.detectChanges();
    expect(root().querySelector('[data-verdict]')?.textContent).toContain('2 problems');
    expect(root().querySelector('[data-verdict]')?.textContent).toContain('Nothing was changed');
    expect(root().querySelectorAll('[data-error]').length).toBe(2);
    expect(root().querySelector('[data-error]')?.textContent).toContain('Asha Rao is already teaching');
    expect(root().querySelectorAll('[data-warning]').length).toBe(1);
  });

  it('Apply to other sections: nothing is created until you preview, then confirm', () => {
    open(LIST);
    (row(1).querySelector('[data-apply]') as HTMLElement).click();
    fixture.detectChanges();
    expect(button('Create 0 drafts').disabled).toBe(true);
    (root().querySelector('[data-pick="11"]') as HTMLInputElement).click();
    (root().querySelector('[data-pick="12"]') as HTMLInputElement).click();
    fixture.detectChanges();
    button('Preview').click();
    const pre = http.expectOne(`${API}/api/Timetables/1/apply-preview`);
    expect(pre.request.body).toEqual({ sectionIds: [11, 12], timetableName: 'Term 1' });
    pre.flush(envelope({
      sourceTimetableId: 1, preview: true, sections: [
        { sectionId: 11, sectionName: 'B', created: true, timetableName: 'Term 1', slotsCopied: 3, slotsNeedingTeacher: 2, slotsSkipped: 0, needsTeacher: [{ dayOfWeek: 1, periodNumber: 1, subjectName: 'Maths', reason: 'The teacher is busy with Class 1 - A then.' }], skipped: [] },
        { sectionId: 12, sectionName: 'C', created: false, skipReason: 'It already has a timetable named "Term 1". Nothing is overwritten.', timetableName: 'Term 1', slotsCopied: 0, slotsNeedingTeacher: 0, slotsSkipped: 0, needsTeacher: [], skipped: [] },
      ],
    }));
    fixture.detectChanges();
    expect(root().querySelector('[data-pv="11"]')?.textContent).toContain('3 slots copied, 2 without a teacher');
    expect(root().querySelector('[data-pv="11"]')?.textContent).toContain('busy with Class 1 - A');
    expect(root().querySelector('[data-pv="12"]')?.textContent).toContain('Skipped');
    expect(button('Create 1 draft').disabled).toBe(false);
    button('Create 1 draft').click();
    const done = http.expectOne(`${API}/api/Timetables/1/apply`);
    expect(done.request.method).toBe('POST');
    done.flush(envelope({ sourceTimetableId: 1, preview: false, sections: [] }));
  });

  it('changing the choice after a preview throws the preview away', () => {
    open(LIST);
    (row(1).querySelector('[data-apply]') as HTMLElement).click();
    fixture.detectChanges();
    (root().querySelector('[data-pick="11"]') as HTMLInputElement).click();
    fixture.detectChanges();
    button('Preview').click();
    http.expectOne(`${API}/api/Timetables/1/apply-preview`).flush(envelope({ sourceTimetableId: 1, preview: true, sections: [{ sectionId: 11, sectionName: 'B', created: true, timetableName: 'Term 1', slotsCopied: 1, slotsNeedingTeacher: 0, slotsSkipped: 0, needsTeacher: [], skipped: [] }] }));
    fixture.detectChanges();
    expect(button('Create 1 draft').disabled).toBe(false);
    (root().querySelector('[data-pick="12"]') as HTMLInputElement).click();
    fixture.detectChanges();
    expect(root().querySelector('[data-preview]')).toBeNull();
    expect(button('Create 0 drafts').disabled).toBe(true);
  });

  it('a section with no timetable says so and offers the first one', () => {
    open([]);
    expect(root().textContent).toContain('no timetable yet');
    (root().querySelector('[data-new]') as HTMLElement).click();
    fixture.detectChanges();
    expect((root().querySelector('[data-name]') as HTMLInputElement).value).toBe('Regular timetable');
  });
});
