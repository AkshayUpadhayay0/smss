import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ClassTeacherAssignment, ClassTeacherList } from '../../models/academic-setup.model';
import { ClassTeacherSetupComponent, dayLabel, historyLine, otherSectionsOf } from './class-teacher-setup.component';

const API = environment.apiUrl;
const envelope = (data: unknown) => ({ status: true, statusCode: 200, message: 'ok', data });

const LIST: ClassTeacherList = {
  academicYearId: 7, academicYearName: '2026-27', unassignedCount: 1,
  sections: [
    { classId: 1, className: 'Class 1', sectionId: 10, sectionName: 'A', assignmentId: 5, employeeId: 'T1', employeeName: 'Asha Rao', hasClassTeacher: true, effectiveFrom: '2026-11-03' },
    { classId: 1, className: 'Class 1', sectionId: 11, sectionName: 'B', hasClassTeacher: false },
  ],
};
const TEACHERS = [{ employeeId: 'T1', fullName: 'Asha Rao', designationName: 'Teacher' }, { employeeId: 'T2', fullName: 'Ravi Nair', designationName: 'Teacher' }];

describe('helpers', () => {
  it('formats a calendar day without time zone maths', () => {
    expect(dayLabel('2026-11-03')).toBe('03 Nov 2026');
    expect(dayLabel(null)).toBe('');
  });
  it('lists the OTHER sections a teacher already has', () => {
    expect(otherSectionsOf(LIST.sections, 'T1', 11)).toEqual(['Class 1 - A']);
    expect(otherSectionsOf(LIST.sections, 'T1', 10)).toEqual([]);
    expect(otherSectionsOf(LIST.sections, 'T9', 10)).toEqual([]);
  });
  it('describes a history line', () => {
    const a = { assignmentId: 1, employeeId: 'T1', employeeName: 'A', statusName: 'Active', effectiveFrom: '2026-11-03' } as ClassTeacherAssignment;
    expect(historyLine(a)).toBe('03 Nov 2026 to now');
    expect(historyLine({ ...a, effectiveTo: '2026-12-01' })).toBe('03 Nov 2026 to 01 Dec 2026');
  });
});

describe('ClassTeacherSetupComponent', () => {
  let fixture: ComponentFixture<ClassTeacherSetupComponent>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;
  const click = (sel: string) => { (root().querySelector(sel) as HTMLElement).click(); fixture.detectChanges(); };
  const button = (label: string) => Array.from(root().querySelectorAll('app-button')).find((b) => b.textContent?.trim() === label)?.querySelector('button') as HTMLButtonElement;

  async function open(readOnly = false) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ClassTeacherSetupComponent);
    fixture.componentRef.setInput('yearId', 7);
    fixture.componentRef.setInput('readOnly', readOnly);
    fixture.detectChanges();
    await fixture.whenStable();
    http.expectOne(`${API}/api/ClassTeacher?academicYearId=7`).flush(envelope(LIST));
    http.expectOne(`${API}/api/ClassTeacher/eligible-teachers`).flush(envelope(TEACHERS));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('shows each section with its current teacher and since when', async () => {
    await open();
    expect(root().querySelector('[data-summary]')?.textContent).toContain('1 of 2 sections');
    expect(root().querySelector('[data-section="10"]')?.textContent).toContain('Asha Rao');
    expect(root().querySelector('[data-section="10"]')?.textContent).toContain('03 Nov 2026');
    expect(root().querySelector('[data-section="11"]')?.textContent).toContain('Not assigned');
  });

  it('a teacher who already has another section needs a confirm, and the confirm is sent', async () => {
    await open();
    click('[data-section="11"] [data-assign]');
    const pick = root().querySelector('[data-pick]') as HTMLSelectElement;
    pick.value = 'T1';
    pick.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(root().querySelector('[data-warning]')?.textContent).toContain('Class 1 - A');
    expect(button('Save').disabled).toBe(true);
    (root().querySelector('[data-confirm]') as HTMLInputElement).click();
    const reason = root().querySelector('[data-reason]') as HTMLTextAreaElement;
    reason.value = 'covers both';
    reason.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(button('Save').disabled).toBe(false);
    button('Save').click();
    const req = http.expectOne(`${API}/api/ClassTeacher/assign`);
    expect(req.request.body).toEqual({ academicYearId: 7, sectionId: 11, employeeId: 'T1', reason: 'covers both', confirmMultipleSections: true });
    req.flush(envelope({ assignmentId: 9 }));
    http.expectOne(`${API}/api/ClassTeacher?academicYearId=7`).flush(envelope(LIST));
    await fixture.whenStable();
  });

  it('a teacher with no other section is saved without a confirm', async () => {
    await open();
    click('[data-section="11"] [data-assign]');
    const pick = root().querySelector('[data-pick]') as HTMLSelectElement;
    pick.value = 'T2';
    pick.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(root().querySelector('[data-warning]')).toBeNull();
    expect(button('Save').disabled).toBe(false);
  });

  it('the history drawer lists who, from, to and why', async () => {
    await open();
    click('[data-section="10"] [data-history]');
    http.expectOne(`${API}/api/ClassTeacher/history?academicYearId=7&sectionId=10`).flush(envelope([
      { assignmentId: 2, employeeId: 'T1', employeeName: 'Asha Rao', statusName: 'Active', effectiveFrom: '2026-11-03', assignReason: 'new session', assignedByName: 'Admin' },
      { assignmentId: 1, employeeId: 'T2', employeeName: 'Ravi Nair', statusName: 'Inactive', effectiveFrom: '2026-06-01', effectiveTo: '2026-11-03', endReason: 'on leave', endedByName: 'Admin' },
    ]));
    await fixture.whenStable();
    fixture.detectChanges();
    const entries = root().querySelectorAll('[data-entry]');
    expect(entries.length).toBe(2);
    expect(entries[0].textContent).toContain('Current');
    expect(entries[1].textContent).toContain('01 Jun 2026 to 03 Nov 2026');
    expect(entries[1].textContent).toContain('on leave');
  });

  it('a closed year hides the change buttons but keeps the history', async () => {
    await open(true);
    expect(root().textContent).toContain('is closed');
    expect(root().querySelector('[data-assign]')).toBeNull();
    expect(root().querySelector('[data-remove]')).toBeNull();
    expect(root().querySelectorAll('[data-history]').length).toBe(2);
  });
});
