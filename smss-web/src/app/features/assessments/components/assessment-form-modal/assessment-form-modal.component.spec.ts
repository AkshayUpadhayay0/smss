import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { AssessmentFormModalComponent } from './assessment-form-modal.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

describe('AssessmentFormModalComponent: numeric marks', () => {
  let fixture: ComponentFixture<AssessmentFormModalComponent>;
  let http: HttpTestingController;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  beforeEach(() => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: ['Teacher'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AssessmentFormModalComponent);
    fixture.componentRef.setInput('academicYearId', 40);
    fixture.componentRef.setInput('types', [{ assessmentTypeId: 1, typeName: 'Class Test', typeCode: 'CT' }]);
    fixture.componentRef.setInput('combos', [{ sectionId: 38, sectionLabel: 'Nursery – A', subjectId: 5, subjectName: 'English' }]);
    fixture.detectChanges();
    cmp()['form'].controls.subjectId.setValue('5');
    http.expectOne((r) => r.url === `${API}/api/Assessment/roster`).flush(env([
      { studentId: 'S1', fullName: 'Aanya Thapa', rollNumber: '1' }, { studentId: 'S2', fullName: 'Harsh Dobhal', rollNumber: '2' }, { studentId: 'S3', fullName: 'Pari Rana', rollNumber: '3' }, { studentId: 'S4', fullName: 'Rohan Bhatt', rollNumber: '4' },
    ]));
    fixture.detectChanges();
    cmp()['form'].controls.maxMarks.setValue('20');
    cmp()['submitted'].set(true);
  });
  afterEach(() => { http.verify(); clearSessions(); });

  it('a number (what a type=number input gives), an empty box (null) and a string are all read without throwing', () => {
    const rows = cmp()['rows'] as { at(i: number): { controls: { marks: { setValue(v: unknown): void } } } };
    rows.at(0).controls.marks.setValue(17);        // number, as typed
    rows.at(1).controls.marks.setValue(null);      // cleared box
    rows.at(2).controls.marks.setValue('');        // empty string
    rows.at(3).controls.marks.setValue('12.5');    // string
    expect(() => cmp()['rowErrors']()).not.toThrow();
    expect([0, 1, 2, 3].map((i) => cmp()['rowInvalid'](i))).toEqual([false, true, true, false]);
    expect(cmp()['rowErrors']()).toBe(2);
  });

  it('numeric marks outside 0..max or with 3 decimals are invalid; 0 is a valid mark', () => {
    const rows = cmp()['rows'] as { at(i: number): { controls: { marks: { setValue(v: unknown): void } } } };
    rows.at(0).controls.marks.setValue(0);
    rows.at(1).controls.marks.setValue(21);
    rows.at(2).controls.marks.setValue(-1);
    rows.at(3).controls.marks.setValue(1.234);
    expect([0, 1, 2, 3].map((i) => cmp()['rowInvalid'](i))).toEqual([false, true, true, true]);
  });

  it('saving with every mark a number posts them as numbers', () => {
    const rows = cmp()['rows'] as { at(i: number): { controls: { marks: { setValue(v: unknown): void } } } };
    [18, 15.5, 0, 20].forEach((v, i) => rows.at(i).controls.marks.setValue(v));
    cmp()['form'].patchValue({ title: 'Unit test 1', assessmentTypeId: '1' });
    cmp()['submit']();
    const r = http.expectOne(`${API}/api/Assessment`);
    expect(r.request.body.marks.map((m: { obtainedMarks: number }) => m.obtainedMarks)).toEqual([18, 15.5, 0, 20]);
    r.flush(env({}));
  });
});
