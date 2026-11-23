import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { SetupSection, SetupSubject, YearClassSubjects } from '../../models/academic-setup.model';
import { SubjectSetupComponent, effectiveCount, toSubjectDraft, toSubjectRequest } from './subject-setup.component';

const API = environment.apiUrl;
const envelope = (data: unknown) => ({ status: true, statusCode: 200, message: 'ok', data });

function subject(over: Partial<SetupSubject>): SetupSubject {
  return { subjectId: 1, subjectName: 'Maths', subjectCode: 'MAT', isActive: true, mapped: true, inUse: 0, inUseText: null, ...over };
}
function section(over: Partial<SetupSection>): SetupSection {
  return { sectionId: 10, sectionName: 'A', extraSubjectIds: [], notTaughtSubjectIds: [], inUseSubjectIds: [], effectiveCount: 2, ...over };
}
function dto(over: Partial<YearClassSubjects> = {}): YearClassSubjects {
  return {
    academicYearId: 7, yearName: '2026-27', isCurrent: true, isClosed: false, classId: 3, className: 'Class 5', classOffered: true,
    subjects: [subject({}), subject({ subjectId: 2, subjectName: 'Science' }), subject({ subjectId: 3, subjectName: 'Art', mapped: false })],
    sections: [section({}), section({ sectionId: 11, sectionName: 'B' })],
    ...over,
  };
}

describe('toSubjectDraft / toSubjectRequest / effectiveCount', () => {
  it('turns the server answer into sets and back, sending only sections that have an exception', () => {
    const d = toSubjectDraft(dto({ sections: [section({ extraSubjectIds: [3] }), section({ sectionId: 11, sectionName: 'B' })] }));
    expect([...d.mapped]).toEqual([1, 2]);
    expect(toSubjectRequest(d)).toEqual({ subjectIds: [1, 2], sections: [{ sectionId: 10, extraSubjectIds: [3], notTaughtSubjectIds: [] }] });
  });
  it('drops an exception the class set makes meaningless', () => {
    const d = toSubjectDraft(dto({ sections: [section({ extraSubjectIds: [3], notTaughtSubjectIds: [2] })] }));
    d.mapped.add(3);      // the class now has Art: "also taught here" is moot
    d.mapped.delete(2);   // and no longer has Science: "not taught here" is moot
    expect(toSubjectRequest(d).sections).toEqual([]);
  });
  it('counts what a section is taught: class set - not taught + extra', () => {
    const d = toSubjectDraft(dto({ sections: [section({ extraSubjectIds: [3], notTaughtSubjectIds: [2] })] }));
    expect(effectiveCount(d, 10)).toBe(2);   // Maths + Art
    expect(effectiveCount(d, 99)).toBe(2);   // unknown section = the class set
  });
});

describe('SubjectSetupComponent', () => {
  let fixture: ComponentFixture<SubjectSetupComponent>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;
  const box = (sel: string) => root().querySelector(sel + ' input[type=checkbox]') as HTMLInputElement;
  const button = (label: string) => Array.from(root().querySelectorAll('app-button')).find((b) => b.textContent?.trim() === label)!.querySelector('button') as HTMLButtonElement;
  const URL = `${API}/api/AcademicSetup/years/7/classes/3/subjects`;

  async function open(d: YearClassSubjects, readOnly = false) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SubjectSetupComponent);
    fixture.componentRef.setInput('yearId', 7);
    fixture.componentRef.setInput('classes', [{ classId: 3, className: 'Class 5' }]);
    fixture.componentRef.setInput('readOnly', readOnly);
    fixture.detectChanges();
    await fixture.whenStable();
    http.expectOne(URL).flush(envelope(d));   // the first offered class is loaded by itself
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('loads the first offered class and shows its subjects for the year', async () => {
    await open(dto());
    expect(root().querySelector('[data-summary]')?.textContent).toContain('2 subjects for Class 5 in 2026-27');
    expect(box('[data-subject="1"]').checked).toBe(true);
    expect(box('[data-subject="3"]').checked).toBe(false);
    expect(button('Save').disabled).toBe(true);   // nothing changed yet
  });

  it('ticking a subject and Save sends the whole set and the exceptions', async () => {
    await open(dto());
    box('[data-subject="3"]').click();
    fixture.detectChanges();
    expect(button('Save').disabled).toBe(false);
    button('Save').click();
    const req = http.expectOne(URL);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ subjectIds: [1, 2, 3], sections: [] });
    req.flush(envelope(dto({ subjects: [subject({}), subject({ subjectId: 2, subjectName: 'Science' }), subject({ subjectId: 3, subjectName: 'Art', mapped: true })] })));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(button('Save').disabled).toBe(true);   // saved: clean again
  });

  it('a subject that is in use cannot be unticked and says what uses it', async () => {
    await open(dto({ subjects: [subject({ inUse: 3, inUseText: '2 timetable slots, 1 homework' }), subject({ subjectId: 2, subjectName: 'Science' })] }));
    expect(box('[data-subject="1"]').disabled).toBe(true);
    expect(root().querySelector('[data-subject="1"]')?.textContent).toContain('2 timetable slots, 1 homework');
    expect(box('[data-subject="2"]').disabled).toBe(false);
  });

  it('a section exception is saved on its section only', async () => {
    await open(dto());
    const sectionA = root().querySelector('[data-sec="10"]') as HTMLElement;
    (sectionA.querySelector('[data-not="2"]') as HTMLInputElement).click();   // Science is not taught in A
    fixture.detectChanges();
    (root().querySelector('[data-sec="11"] [data-extra="3"]') as HTMLInputElement).click();   // Art is also taught in B
    fixture.detectChanges();
    button('Save').click();
    const req = http.expectOne(URL);
    expect(req.request.body).toEqual({ subjectIds: [1, 2], sections: [
      { sectionId: 10, extraSubjectIds: [], notTaughtSubjectIds: [2] },
      { sectionId: 11, extraSubjectIds: [3], notTaughtSubjectIds: [] },
    ] });
    req.flush(envelope(dto()));
    await fixture.whenStable();
  });

  it('a subject used inside one section cannot be marked not taught there', async () => {
    await open(dto({ sections: [section({ inUseSubjectIds: [2] }), section({ sectionId: 11, sectionName: 'B' })] }));
    expect((root().querySelector('[data-sec="10"] [data-not="2"]') as HTMLInputElement).disabled).toBe(true);
    expect((root().querySelector('[data-sec="11"] [data-not="2"]') as HTMLInputElement).disabled).toBe(false);
  });

  it('a closed year is read-only', async () => {
    await open(dto({ isClosed: true }));
    expect(root().textContent).toContain('is closed');
    expect(box('[data-subject="1"]').disabled).toBe(true);
    expect(button('Save').disabled).toBe(true);
  });

  it('a class that is not offered cannot be saved', async () => {
    await open(dto({ classOffered: false, sections: [] }));
    box('[data-subject="3"]').click();
    fixture.detectChanges();
    expect(button('Save').disabled).toBe(true);
  });
});
