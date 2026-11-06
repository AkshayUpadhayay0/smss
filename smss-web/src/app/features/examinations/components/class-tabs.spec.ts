import { Component, signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../core/testing/auth-test-utils';
import { ClassActivityTabComponent } from './class-activity-tab/class-activity-tab.component';
import { ClassPromotionTabComponent } from './class-promotion-tab/class-promotion-tab.component';
import { ClassRemarksTabComponent } from './class-remarks-tab/class-remarks-tab.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

@Component({ imports: [ClassActivityTabComponent, ClassRemarksTabComponent, ClassPromotionTabComponent], template: `
  @switch (which()) {
    @case ('activity') { <app-class-activity-tab [termId]="5" [sectionId]="43" /> }
    @case ('remarks') { <app-class-remarks-tab [termId]="5" [sectionId]="43" /> }
    @case ('promotion') { <app-class-promotion-tab [schemeId]="1" [sectionId]="43" /> }
  }` })
class HostComponent { readonly which = signal<'activity' | 'remarks' | 'promotion'>('activity'); }

describe('class report tabs', () => {
  let fixture: ComponentFixture<HostComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const button = (label: string) => [...el().querySelectorAll('button')].find((b) => b.textContent?.includes(label)) as HTMLButtonElement | undefined;
  function host(which: 'activity' | 'remarks' | 'promotion') {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: ['Teacher'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.which.set(which);
    fixture.detectChanges();
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('activity grades: change a grade, save, and the body carries every student x area (an unset one as empty = cleared)', () => {
    host('activity');
    http.expectOne((r) => r.url === `${API}/api/ReportClass/activity-grades`).flush(env({ termId: 5, termName: 'Term 1', sectionId: 43, locked: false, canEdit: true,
      areas: [{ areaId: 1, category: 'DISCIPLINE', name: 'Punctuality', options: [{ code: 'A', label: 'Always' }, { code: 'B', label: 'Often' }] }, { areaId: 2, category: 'CO_SCHOLASTIC', name: 'Art', options: [{ code: 'A', label: 'Excellent' }] }],
      students: [{ studentId: 'S1', rollNumber: '1', fullName: 'Asha Rawat', grades: { '2': 'A' } }] }));
    fixture.detectChanges();
    expect(button('Save activity grades')!.disabled).toBe(true);
    const sel = el().querySelector<HTMLSelectElement>('[aria-label="Asha Rawat Punctuality"]')!; sel.value = 'B'; sel.dispatchEvent(new Event('change')); fixture.detectChanges();
    button('Save activity grades')!.click();
    const req = http.expectOne(`${API}/api/ReportClass/activity-grades`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ termId: 5, sectionId: 43, grades: [{ studentId: 'S1', areaId: 1, gradeCode: 'B' }, { studentId: 'S1', areaId: 2, gradeCode: 'A' }] });
  });

  it('activity grades: a locked term shows the reason and cannot be edited', () => {
    host('activity');
    http.expectOne((r) => r.url === `${API}/api/ReportClass/activity-grades`).flush(env({ termId: 5, termName: 'Term 1', sectionId: 43, locked: true, canEdit: false, readOnlyReason: 'This term is locked for the section. A School Admin has to unlock it before anything can change.',
      areas: [{ areaId: 1, category: 'DISCIPLINE', name: 'Punctuality', options: [{ code: 'A', label: 'Always' }] }], students: [{ studentId: 'S1', rollNumber: '1', fullName: 'Asha Rawat', grades: {} }] }));
    fixture.detectChanges();
    expect(text()).toContain('This term is locked');
    expect(el().querySelector<HTMLSelectElement>('select')!.disabled).toBe(true);
    expect(button('Save activity grades')).toBeUndefined();
  });

  it('activity grades: with no areas set up it says where to add them', () => {
    host('activity');
    http.expectOne((r) => r.url === `${API}/api/ReportClass/activity-grades`).flush(env({ termId: 5, termName: 'T', sectionId: 43, locked: false, canEdit: true, areas: [], students: [] }));
    fixture.detectChanges();
    expect(text()).toContain('No activity areas are set up yet');
  });

  const TERM = (src: 'AUTO' | 'MANUAL', canEdit = true) => ({ termId: 5, termName: 'Term 1', sectionId: 43, attendanceSource: src, evaluationMode: 'MARKS', locked: false, canEdit,
    students: [{ studentId: 'S1', rollNumber: '1', fullName: 'Asha Rawat', remark: 'Good', conduct: 'Good', rankText: null, manualWorkingDays: 100, manualPresentDays: 90 }] });
  const ATT = (src: string, warning: string | null) => ({ termId: 5, termName: 'Term 1', sectionId: 43, from: '2026-04-01', to: '2026-09-29', today: '2026-11-06', source: src, workingDays: 120, unmarkedDays: 2, unmarkedDates: ['2026-05-13', '2026-06-13'], warning,
    students: [{ studentId: 'S1', rollNumber: '1', fullName: 'Asha Rawat', workingDays: 118, presentDays: 100, absentDays: 18, notMarkedDays: 2, percentage: 84.7 }] });

  it('remarks and attendance (AUTO): shows the Attendance module\'s numbers read-only, the warning about unmarked days, and saves remarks', () => {
    host('remarks');
    http.expectOne((r) => r.url === `${API}/api/ReportClass/term-students`).flush(env(TERM('AUTO')));
    http.expectOne((r) => r.url === `${API}/api/ReportClass/attendance`).flush(env(ATT('AUTO', '2 working days so far have no attendance marked for anyone in this section. They are not counted.')));
    fixture.detectChanges();
    expect(text()).toContain('read from the Attendance module');
    expect(text()).toContain('2 working days so far have no attendance marked');
    expect(text()).toContain('100'); expect(text()).toContain('118'); expect(text()).toContain('84.7');
    expect(el().querySelector('[aria-label="Asha Rawat working days"]')).toBeNull();
    const remark = el().querySelector<HTMLInputElement>('[aria-label="Asha Rawat remark"]')!; remark.value = 'Needs to read more'; remark.dispatchEvent(new Event('input')); fixture.detectChanges();
    button('Save remarks and attendance')!.click();
    const req = http.expectOne(`${API}/api/ReportClass/term-students`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.students[0]).toEqual({ studentId: 'S1', remark: 'Needs to read more', conduct: 'Good', rankText: null, manualWorkingDays: 100, manualPresentDays: 90 });
  });

  it('remarks and attendance (MANUAL): working and present days are typed in, and sent as numbers', () => {
    host('remarks');
    http.expectOne((r) => r.url === `${API}/api/ReportClass/term-students`).flush(env(TERM('MANUAL')));
    http.expectOne((r) => r.url === `${API}/api/ReportClass/attendance`).flush(env(ATT('MANUAL', null)));
    fixture.detectChanges();
    const w = el().querySelector<HTMLInputElement>('[aria-label="Asha Rawat working days"]')!; w.value = '110'; w.dispatchEvent(new Event('input'));
    const p = el().querySelector<HTMLInputElement>('[aria-label="Asha Rawat present days"]')!; p.value = '104'; p.dispatchEvent(new Event('input')); fixture.detectChanges();
    button('Save remarks and attendance')!.click();
    const body = http.expectOne(`${API}/api/ReportClass/term-students`).request.body;
    expect(body.students[0].manualWorkingDays).toBe(110);
    expect(body.students[0].manualPresentDays).toBe(104);
  });

  it('remarks and attendance: the Principal\'s read-only view has no inputs to change and no save', () => {
    host('remarks');
    http.expectOne((r) => r.url === `${API}/api/ReportClass/term-students`).flush(env({ ...TERM('AUTO', false), readOnlyReason: 'The Principal can read these inputs but not change them.' }));
    http.expectOne((r) => r.url === `${API}/api/ReportClass/attendance`).flush(env(ATT('AUTO', null)));
    fixture.detectChanges();
    expect(text()).toContain('The Principal can read these inputs');
    expect([...el().querySelectorAll<HTMLInputElement>('input')].every((i) => i.disabled)).toBe(true);
    expect(button('Save remarks and attendance')).toBeUndefined();
  });

  it('promotion: decision, promoted-to and new session date are sent per student', () => {
    host('promotion');
    http.expectOne((r) => r.url === `${API}/api/ReportClass/year-students`).flush(env({ schemeId: 1, sectionId: 43, locked: false, canEdit: true, students: [{ studentId: 'S1', rollNumber: '1', fullName: 'Asha Rawat', promotionDecision: null, promotedTo: null, newSessionDate: null }] }));
    fixture.detectChanges();
    const d = el().querySelector<HTMLSelectElement>('[aria-label="Asha Rawat decision"]')!; d.value = 'PROMOTED'; d.dispatchEvent(new Event('change'));
    const to = el().querySelector<HTMLInputElement>('[aria-label="Asha Rawat promoted to"]')!; to.value = 'Class 4'; to.dispatchEvent(new Event('input'));
    const dt = el().querySelector<HTMLInputElement>('[aria-label="Asha Rawat new session date"]')!; dt.value = '2027-04-01'; dt.dispatchEvent(new Event('change')); fixture.detectChanges();
    button('Save promotion')!.click();
    const req = http.expectOne(`${API}/api/ReportClass/year-students`);
    expect(req.request.body).toEqual({ schemeId: 1, sectionId: 43, students: [{ studentId: 'S1', promotionDecision: 'PROMOTED', promotedTo: 'Class 4', newSessionDate: '2027-04-01' }] });
  });

  it('promotion: once the last term is locked the decisions are read-only, with the reason', () => {
    host('promotion');
    http.expectOne((r) => r.url === `${API}/api/ReportClass/year-students`).flush(env({ schemeId: 1, sectionId: 43, locked: true, canEdit: false, readOnlyReason: 'The last term of this scheme is locked for the section, so the year\'s promotion decisions are final until a School Admin unlocks it.', students: [{ studentId: 'S1', rollNumber: '1', fullName: 'Asha Rawat', promotionDecision: 'PROMOTED', promotedTo: 'Class 4', newSessionDate: '2027-04-01' }] }));
    fixture.detectChanges();
    expect(text()).toContain('promotion decisions are final');
    expect(el().querySelector<HTMLSelectElement>('select')!.disabled).toBe(true);
    expect(button('Save promotion')).toBeUndefined();
  });
});
