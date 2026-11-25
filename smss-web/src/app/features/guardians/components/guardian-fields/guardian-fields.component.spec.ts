import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, FormGroup, Validators } from '@angular/forms';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { GuardianPicks, SiblingStudent, picksToRequest } from '../../models/guardian.model';
import { GuardianFieldsComponent } from './guardian-fields.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const SECTIONS = [{ sectionId: 31, schoolId: 'S', classId: 3, className: 'Class 3', sectionName: 'A', statusName: 'Active' }];
const guardian = (id: number, name: string, over: Record<string, unknown> = {}) => ({ guardianId: id, name, relationship: 'MOTHER', maskedMobile: '98XXXXXX10', hasWhatsApp: false, hasEmail: false, isPrimary: id === 1, ...over });
const RESULTS: SiblingStudent[] = [
  { studentId: 'S1', studentName: 'Aarav Rao', className: 'Class 3', sectionName: 'A', guardians: [guardian(1, 'Meera Rao'), guardian(2, 'Anil Rao', { relationship: 'FATHER', hasWhatsApp: true, hasEmail: true })] },
];

const newForm = () =>
  new FormGroup({
    guardianName: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    guardianRelationship: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    guardianMobileNumber: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    guardianWhatsAppNumber: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    guardianWhatsAppSameAsMobile: new FormControl(false, { nonNullable: true }),
    guardianEmail: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

describe('GuardianFieldsComponent (the sibling pick)', () => {
  let fixture: ComponentFixture<GuardianFieldsComponent>;
  let http: HttpTestingController;
  let form: ReturnType<typeof newForm>;
  let emitted: GuardianPicks[];
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  function open(initial: GuardianPicks | null = null) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: ['Admission Staff'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    form = newForm();
    emitted = [];
    fixture = TestBed.createComponent(GuardianFieldsComponent);
    fixture.componentRef.setInput('form', form);
    fixture.componentRef.setInput('initial', initial);
    fixture.componentInstance.picksChange.subscribe((p) => emitted.push(p));
    fixture.detectChanges();
    http.expectOne(`${API}/api/Sections`).flush(env(SECTIONS));
    fixture.detectChanges();
  }
  async function search() {
    cmp()['open'].set(true);
    cmp()['classControl'].setValue('3');
    cmp()['search']();
    http.expectOne((r) => r.url === `${API}/api/Guardians/siblings`).flush(env(RESULTS));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('starts with the typed-in guardian fields and no search until "Has a sibling" is ticked', () => {
    open();
    expect(el().querySelector('[data-section="sibling-search"]')).toBeNull();
    expect(el().querySelector('[data-note="linked-primary"]')).toBeNull();
    expect(form.controls.guardianName.enabled).toBe(true);
    cmp()['open'].set(true);
    fixture.detectChanges();
    expect(el().querySelector('[data-section="sibling-search"]')).not.toBeNull();
  });

  it('a search needs a class or two letters; nothing is sent otherwise; the class and the name go to the server', () => {
    open();
    cmp()['open'].set(true);
    cmp()['search']();
    fixture.detectChanges();
    http.expectNone((r) => r.url === `${API}/api/Guardians/siblings`);
    expect(el().querySelector('[data-note="sibling-error"]')!.textContent).toContain('at least 2 letters');
    cmp()['classControl'].setValue('3');
    cmp()['searchControl'].setValue(' aar ');
    cmp()['search']();
    const req = http.expectOne((r) => r.url === `${API}/api/Guardians/siblings`);
    expect(req.request.params.get('classId')).toBe('3');
    expect(req.request.params.get('search')).toBe('aar');
    req.flush(env([]));
    fixture.detectChanges();
    expect(el().querySelector('[data-note="sibling-none"]')).not.toBeNull();
  });

  it('shows the students with their parents (masked numbers only) and ticking the first parent makes them the primary guardian', async () => {
    open();
    await search();
    expect(el().querySelector('[data-student="S1"]')!.textContent).toContain('98XXXXXX10');
    cmp()['toggle'](RESULTS[0].guardians[0]);
    fixture.detectChanges();
    expect(cmp()['primaryId']()).toBe(1);
    expect(emitted.at(-1)).toMatchObject({ primary: { guardianId: 1 }, extras: [] });
    // name and mobile come from the record: not typed, their controls are off
    expect(form.controls.guardianName.disabled).toBe(true);
    expect(form.controls.guardianMobileNumber.disabled).toBe(true);
    expect(el().querySelector('[data-note="linked-primary"]')!.textContent).toContain('Meera Rao');
    // this guardian has neither on record: both are still asked for
    expect(form.controls.guardianEmail.enabled).toBe(true);
    expect(form.controls.guardianWhatsAppNumber.enabled).toBe(true);
    expect(el().querySelector('[data-note="email-on-file"]')).toBeNull();
  });

  it('a guardian with WhatsApp and email on record is not asked for them again; the stored number is never shown whole', async () => {
    open();
    await search();
    cmp()['toggle'](RESULTS[0].guardians[1]);
    fixture.detectChanges();
    expect(form.controls.guardianEmail.disabled).toBe(true);
    expect(form.controls.guardianWhatsAppNumber.disabled).toBe(true);
    expect(form.controls.guardianWhatsAppSameAsMobile.disabled).toBe(true);
    expect(el().querySelector('[data-note="whatsapp-on-file"]')).not.toBeNull();
    expect(el().querySelector('[data-note="email-on-file"]')).not.toBeNull();
    expect(el().textContent).not.toMatch(/\b\d{10}\b/);
  });

  it('the admin chooses the primary: a second ticked parent is an extra until chosen, and "a new person" turns the typed fields back on', async () => {
    open();
    await search();
    cmp()['toggle'](RESULTS[0].guardians[0]);
    cmp()['toggle'](RESULTS[0].guardians[1]);
    expect(emitted.at(-1)).toMatchObject({ primary: { guardianId: 1 }, extras: [{ guardianId: 2 }] });
    cmp()['makePrimary'](2);
    expect(emitted.at(-1)).toMatchObject({ primary: { guardianId: 2 }, extras: [{ guardianId: 1 }] });
    cmp()['makePrimary'](null);
    fixture.detectChanges();
    expect(emitted.at(-1)).toMatchObject({ primary: null });
    expect(emitted.at(-1)!.extras.map((g) => g.guardianId).sort()).toEqual([1, 2]);
    expect(form.controls.guardianName.enabled).toBe(true);
    expect(form.controls.guardianEmail.enabled).toBe(true);
    cmp()['remove'](1);
    cmp()['remove'](2);
    expect(emitted.at(-1)).toEqual({ primary: null, extras: [] });
  });

  it('an edited application starts from its saved pick', () => {
    open({ primary: { guardianId: 2, name: 'Anil Rao', relationship: 'FATHER', maskedMobile: '98XXXXXX10', hasWhatsApp: true, hasEmail: true }, extras: [{ guardianId: 1, name: 'Meera Rao', relationship: 'MOTHER', maskedMobile: '97XXXXXX11', hasWhatsApp: false, hasEmail: false }] });
    fixture.detectChanges();
    expect(cmp()['primaryId']()).toBe(2);
    expect(el().querySelectorAll('[data-picked]').length).toBe(2);
    expect(form.controls.guardianName.disabled).toBe(true);
  });

  it('the request carries only the ids and the relationships of the pick', () => {
    expect(picksToRequest(null)).toEqual({ existingGuardianId: null, additionalGuardians: [] });
    expect(picksToRequest({ primary: { guardianId: 2, name: 'A', relationship: 'FATHER', maskedMobile: 'x', hasWhatsApp: true, hasEmail: true }, extras: [{ guardianId: 1, name: 'B', relationship: 'MOTHER', maskedMobile: 'y', hasWhatsApp: false, hasEmail: false }] }))
      .toEqual({ existingGuardianId: 2, additionalGuardians: [{ guardianId: 1, relationship: 'MOTHER' }] });
  });
});
