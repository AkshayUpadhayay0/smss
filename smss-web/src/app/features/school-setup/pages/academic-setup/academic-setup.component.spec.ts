import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { OfferingClass, OfferingSection, YearOffering } from '../../models/academic-setup.model';
import { AcademicSetupComponent, parseCapacity, toDrafts, toRequest } from './academic-setup.component';

const API = environment.apiUrl;
const envelope = (data: unknown) => ({ status: true, statusCode: 200, message: 'ok', data });

function section(over: Partial<OfferingSection>): OfferingSection {
  return { sectionId: 1, sectionName: 'A', isActive: true, offered: true, masterMaxStrength: 40, maxStrength: null, effectiveMaxStrength: 40, enrolments: 0, hasClassTeacher: false, timetableEntries: 0, ...over };
}
function cls(over: Partial<OfferingClass>): OfferingClass {
  return { classId: 1, className: 'Class 1', sequenceOrder: 1, isActive: true, offered: true, enrolments: 0, hasFeeStructure: false, sections: [section({})], ...over };
}
function offering(over: Partial<YearOffering> = {}): YearOffering {
  return { academicYearId: 7, yearName: '2026-27', isCurrent: true, isClosed: false, offeredClasses: 2, offeredSections: 2, classes: [cls({}), cls({ classId: 2, className: 'Class 2', sections: [section({ sectionId: 2 })] })], ...over };
}
const YEARS = [
  { academicYearId: 6, yearName: '2025-26', isCurrent: false, isClosed: false, statusName: 'Active', startDate: '2025-04-01', endDate: '2026-03-31' },
  { academicYearId: 7, yearName: '2026-27', isCurrent: true, isClosed: false, statusName: 'Active', startDate: '2026-04-01', endDate: '2027-03-31' },
];

describe('parseCapacity', () => {
  it('empty means "use the section master\'s capacity"', () => expect(parseCapacity('  ')).toBeNull());
  it('accepts a whole number from 1 to 32767', () => { expect(parseCapacity('40')).toBe(40); expect(parseCapacity('32767')).toBe(32767); });
  it('refuses 0, negatives, decimals and words', () => ['0', '-3', '2.5', 'abc', '32768'].forEach((t) => expect(Number.isNaN(parseCapacity(t))).toBe(true)));
});

describe('toDrafts / toRequest', () => {
  it('locks what the year already uses and says why', () => {
    const d = toDrafts(offering({ classes: [cls({ hasFeeStructure: true, sections: [section({ enrolments: 3, hasClassTeacher: true })] })] }));
    expect(d[0].sections[0].lockedReason).toBe('3 students are enrolled, it has a class teacher');
    expect(d[0].lockedReason).toContain('fee structure');
    expect(d[0].lockedReason).toContain('section A is in use');
  });
  it('does not lock something that is not offered', () => {
    const d = toDrafts(offering({ classes: [cls({ offered: false, hasFeeStructure: true, sections: [section({ offered: false, enrolments: 1 })] })] }));
    expect(d[0].lockedReason).toBeNull();
    expect(d[0].sections[0].lockedReason).toBeNull();
  });
  it('sends only what is offered, with an empty capacity as null', () => {
    const d = toDrafts(offering({ classes: [cls({ sections: [section({ sectionId: 5, maxStrength: 35 }), section({ sectionId: 6, sectionName: 'B' }), section({ sectionId: 7, sectionName: 'C', offered: false })] }), cls({ classId: 2, offered: false })] }));
    expect(toRequest(d)).toEqual({ classes: [{ classId: 1, sections: [{ sectionId: 5, maxStrength: 35 }, { sectionId: 6, maxStrength: null }] }] });
  });
});

describe('AcademicSetupComponent', () => {
  let fixture: ComponentFixture<AcademicSetupComponent>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;
  const box = (sel: string) => root().querySelector(sel + ' input[type=checkbox]') as HTMLInputElement;
  const button = (label: string) => Array.from(root().querySelectorAll('app-button')).find((b) => b.textContent?.trim() === label)!.querySelector('button') as HTMLButtonElement;

  async function open(o: YearOffering, log: unknown[] = []) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AcademicSetupComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/AcademicYear`).flush(envelope(YEARS));
    fixture.detectChanges();
    http.expectOne(`${API}/api/AcademicSetup/years/7/copy-log`).flush(envelope(log));   // the last copy into this year (a note only)
    http.expectOne(`${API}/api/AcademicSetup/years/7/classes`).flush(envelope(o));   // the CURRENT year is the default
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('opens on the current year and shows what it offers', async () => {
    await open(offering());
    expect(root().querySelector('[data-summary]')?.textContent).toContain('2 classes, 2 sections offered');
    expect(box('[data-class="1"]').checked).toBe(true);
    expect(button('Save').disabled).toBe(true);   // nothing changed yet
  });

  it('shows the last copy into the year when there is one', async () => {
    await open(offering(), [{ copyId: 1, sourceYearId: 6, sourceYearName: '2025-26', targetYearId: 7, parts: ['SUBJECTS'], status: 'COMPLETED', runByName: 'Admin', startedAt: '2026-11-23T10:00:00Z', finishedAt: '2026-11-23T10:00:05Z', result: [{ part: 'SUBJECTS', label: 'Subjects', created: 12, details: [], skippedExisting: 0, skipped: [], skippedWithReason: 0, notes: [] }] }]);
    expect(root().querySelector('[data-last-copy]')?.textContent).toContain('copied from 2025-26');
    expect(root().querySelector('[data-last-copy]')?.textContent).toContain('12 rows added');
  });

  it('shows no copy note when nothing was ever copied', async () => {
    await open(offering());
    expect(root().querySelector('[data-last-copy]')).toBeNull();
  });

  it('unticking a class that is not in use withdraws its sections and Save sends the whole set', async () => {
    await open(offering());
    box('[data-class="2"]').click();
    fixture.detectChanges();
    expect(root().querySelector('[data-summary]')?.textContent).toContain('1 class, 1 section offered');
    expect(button('Save').disabled).toBe(false);
    button('Save').click();
    const req = http.expectOne(`${API}/api/AcademicSetup/years/7/classes`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ classes: [{ classId: 1, sections: [{ sectionId: 1, maxStrength: null }] }] });
    req.flush(envelope(offering({ offeredClasses: 1, offeredSections: 1, classes: [cls({}), cls({ classId: 2, className: 'Class 2', offered: false, sections: [section({ sectionId: 2, offered: false })] })] })));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(button('Save').disabled).toBe(true);   // saved: clean again
  });

  it('a class or section in use cannot be unticked', async () => {
    await open(offering({ classes: [cls({ classId: 1, sections: [section({ enrolments: 4 })] })], offeredClasses: 1, offeredSections: 1 }));
    expect(box('[data-class="1"]').disabled).toBe(true);
    expect(box('[data-section="1"]').disabled).toBe(true);
    expect(root().textContent).toContain('In use');
  });

  it('ticking a section whose class is off offers the class too', async () => {
    await open(offering({ classes: [cls({ offered: false, sections: [section({ offered: false })] })], offeredClasses: 0, offeredSections: 0 }));
    box('[data-section="1"]').click();
    fixture.detectChanges();
    expect(box('[data-class="1"]').checked).toBe(true);
    expect(root().querySelector('[data-summary]')?.textContent).toContain('1 class, 1 section offered');
  });

  it('a capacity that is not a whole number blocks Save and says so', async () => {
    await open(offering());
    const cap = root().querySelector('[data-section="1"] .capacity input') as HTMLInputElement;
    cap.value = '12.5';
    cap.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(root().textContent).toContain('Capacity must be a whole number');
    expect(button('Save').disabled).toBe(true);
    cap.value = '45';
    cap.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(button('Save').disabled).toBe(false);
  });

  it('a closed year is read-only', async () => {
    await open(offering({ isClosed: true }));
    expect(root().textContent).toContain('is closed');
    expect(box('[data-class="1"]').disabled).toBe(true);
    expect(button('Offer all').disabled).toBe(true);
    expect(button('Save').disabled).toBe(true);
  });

  it('a class that is inactive cannot be newly offered', async () => {
    await open(offering({ classes: [cls({ offered: false, isActive: false, sections: [] })], offeredClasses: 0, offeredSections: 0 }));
    expect(box('[data-class="1"]').disabled).toBe(true);
    expect(root().textContent).toContain('Inactive');
  });
});
