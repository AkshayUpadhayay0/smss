import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ToastService } from '../../../../core/services/toast.service';
import { API, envelope, flushLookups, makeSchool, setupHttp } from '../../testing/school-test-utils';
import { SchoolFormComponent } from './school-form.component';

/* eslint-disable @typescript-eslint/no-explicit-any */
describe('SchoolFormComponent', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<SchoolFormComponent>;
  let cmp: any; // protected members are exercised directly

  function create(mode: 'add' | 'edit' | 'view', schoolId?: string) {
    fixture = TestBed.createComponent(SchoolFormComponent);
    fixture.componentRef.setInput('mode', mode);
    if (schoolId) fixture.componentRef.setInput('schoolId', schoolId);
    cmp = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(() => {
    http = setupHttp();
  });
  afterEach(() => http.verify());

  describe('add mode', () => {
    beforeEach(() => {
      create('add');
      flushLookups(http);
      fixture.detectChanges();
    });

    it('starts with State/District/City disabled and School Status absent from the form', () => {
      const c = cmp.form.controls;
      expect(c.stateId.disabled).toBe(true);
      expect(c.districtId.disabled).toBe(true);
      expect(c.cityId.disabled).toBe(true);
      expect(Object.keys(c)).not.toContain('schoolStatusId');
      expect(cmp.form.controls.schoolCode.enabled).toBe(true);
    });

    it('cascades Country -> State -> District -> City and resets children when a parent changes', () => {
      const c = cmp.form.controls;

      c.countryId.setValue('1');
      expect(flushLookups(http)).toEqual(['states/1']);
      expect(c.stateId.enabled).toBe(true);
      expect(c.districtId.disabled).toBe(true);

      c.stateId.setValue('2');
      expect(flushLookups(http)).toEqual(['districts/1/2']);
      expect(c.districtId.enabled).toBe(true);
      expect(c.cityId.disabled).toBe(true);

      c.districtId.setValue('3');
      expect(flushLookups(http)).toEqual(['cities/1/2/3']);
      c.cityId.setValue('4');
      expect(c.cityId.enabled).toBe(true);

      // Changing the country wipes and disables everything below it.
      c.countryId.setValue('9');
      flushLookups(http);
      expect(c.stateId.value).toBe('');
      expect(c.districtId.value).toBe('');
      expect(c.cityId.value).toBe('');
      expect(c.districtId.disabled).toBe(true);
      expect(c.cityId.disabled).toBe(true);
      expect(cmp.districtOptions()).toEqual([]);
      expect(cmp.cityOptions()).toEqual([]);

      // Clearing the country disables the state too.
      c.countryId.setValue('');
      expect(c.stateId.disabled).toBe(true);
    });

    /** A UDISE code, a name and the (mandatory) first contact, which the form pre-creates as primary. */
    function fillValid() {
      cmp.form.patchValue({ schoolCode: '09010100101', schoolName: '  Green Valley  ' });
      cmp.contacts.at(0).patchValue({ contactType: 'Principal', contactName: 'A. Sharma', email: 'a@example.com', mobileNumber: '9876543211' });
    }

    it('requires an 11-digit UDISE code (digits only, leading zeros allowed)', () => {
      const code = cmp.form.controls.schoolCode;
      for (const bad of ['', '1234567890', '123456789012', '0901010010a', 'GVPS-01', '0901 0100101']) {
        code.setValue(bad);
        expect([bad, code.invalid]).toEqual([bad, true]);
      }
      for (const good of ['09010100101', '12345678901', ' 09010100101 ']) {
        code.setValue(good);
        expect([good, code.valid]).toEqual([good, true]);
      }
      code.setValue('123');
      code.markAsTouched();
      expect(cmp.err('schoolCode')).toBe('UDISE code must be exactly 11 digits');
    });

    it('labels the field "UDISE code"', () => {
      expect(fixture.nativeElement.textContent).toContain('UDISE code');
      expect(fixture.nativeElement.textContent).not.toContain('School code');
    });

    it('starts with one contact, pre-marked as the primary, and cannot remove the last one', () => {
      expect(cmp.contacts.length).toBe(1);
      expect(cmp.contacts.at(0).controls.isPrimary.value).toBe(true);
      expect(cmp.canRemove(cmp.contacts.at(0))).toBe(false);
      cmp.addContact();
      expect(cmp.canRemove(cmp.contacts.at(0))).toBe(true);
      cmp.removeContact(1);
      expect(cmp.canRemove(cmp.contacts.at(0))).toBe(false);
    });

    it('email and mobile are mandatory for every contact; alternate mobile and designation are optional', () => {
      fillValid();
      const row = cmp.contacts.at(0);
      for (const blank of ['', '   ']) {
        row.patchValue({ email: blank, mobileNumber: blank });
        expect(['email', blank, row.controls.email.hasError('required')]).toEqual(['email', blank, true]);
        expect(['mobile', blank, row.controls.mobileNumber.hasError('required')]).toEqual(['mobile', blank, true]);
      }
      row.patchValue({ email: 'not-an-email', mobileNumber: '1234567890' });
      expect(row.controls.email.invalid).toBe(true);
      expect(row.controls.mobileNumber.invalid).toBe(true);

      cmp.submit();
      http.expectNone(`${API}/api/SchoolRegistration/register`);
      expect(cmp.cerr(row, 'email')).toBe('Enter a valid email address');
      expect(cmp.cerr(row, 'mobileNumber')).toBe('Enter a valid 10-digit mobile number');

      row.patchValue({ email: '', mobileNumber: '' });
      expect(cmp.cerr(row, 'email')).toBe('This field is required');
      expect(cmp.cerr(row, 'mobileNumber')).toBe('This field is required');

      row.patchValue({ email: 'ok@example.com', mobileNumber: '9876543210', designation: '', alternateMobileNumber: '' });
      expect(row.valid).toBe(true);
    });

    it('marks Email and Mobile as required in the contact rows', () => {
      const labels = [...(fixture.nativeElement as HTMLElement).querySelectorAll('fieldset.contact label')].map((l) => l.textContent?.replace(/\s+/g, ' ').trim());
      expect(labels).toEqual(expect.arrayContaining(['Email*', 'Mobile number*', 'Name*', 'Contact type*']));
      expect(labels).toContain('Designation');
      expect(labels).toContain('Alternate mobile');
    });

    it('will not register without a completed primary contact', () => {
      cmp.form.patchValue({ schoolCode: '09010100101', schoolName: 'Green Valley' }); // contact left blank
      cmp.submit();
      http.expectNone(`${API}/api/SchoolRegistration/register`);
      expect(cmp.contacts.at(0).invalid).toBe(true);
      expect(cmp.cerr(cmp.contacts.at(0), 'contactName')).toBe('This field is required');
    });

    it('blocks submit when contacts exist but none (or several) is primary, with a clear message', () => {
      fillValid();
      cmp.addContact();
      cmp.contacts.at(1).patchValue({ contactType: 'Owner', contactName: 'B', email: 'b@example.com', mobileNumber: '9876543212' });
      cmp.contacts.at(0).controls.isPrimary.setValue(false); // now nobody is primary
      expect(cmp.contacts.errors?.['primary']).toBeTruthy();

      cmp.submit();
      http.expectNone(`${API}/api/SchoolRegistration/register`);
      expect(cmp.primaryError()).toMatch(/exactly one contact/i);

      cmp.setPrimary(1);
      expect(cmp.contacts.errors).toBeNull();
      expect(cmp.contacts.at(0).controls.isPrimary.value).toBe(false);
      expect(cmp.contacts.at(1).controls.isPrimary.value).toBe(true);
    });

    it('an added contact is not primary; removing the primary promotes the next one', () => {
      cmp.addContact();
      expect(cmp.contacts.at(0).controls.isPrimary.value).toBe(true);
      expect(cmp.contacts.at(1).controls.isPrimary.value).toBe(false);
      cmp.removeContact(0);
      expect(cmp.contacts.length).toBe(1);
      expect(cmp.contacts.at(0).controls.isPrimary.value).toBe(true);
    });

    it('does not submit invalid fields (code pattern, mobile, GSTIN, PAN, pincode, website)', () => {
      cmp.form.patchValue({
        schoolCode: 'bad code!',
        schoolName: 'X',
        mobileNumber: '1234567890',
        schoolGstin: 'nope',
        schoolPan: 'nope',
        pincode: '012345',
        website: 'example.com',
      });
      cmp.submit();
      http.expectNone(`${API}/api/SchoolRegistration/register`);
      const c = cmp.form.controls;
      for (const name of ['schoolCode', 'mobileNumber', 'schoolGstin', 'schoolPan', 'pincode', 'website']) {
        expect([name, c[name].invalid]).toEqual([name, true]);
      }
    });

    it('sends blanks as null, uppercases GSTIN/PAN, converts ids to numbers and omits status/logo fields', () => {
      fillValid();
      cmp.form.patchValue({ schoolGstin: '07aabcu9603r1zm', schoolPan: 'aabcu9603r', boardTypeId: '1', schoolEstablishYear: '1998' });
      cmp.contacts.at(0).patchValue({ alternateMobileNumber: '' });

      cmp.submit();
      const req = http.expectOne(`${API}/api/SchoolRegistration/register`);
      const body = req.request.body;
      expect(body.schoolCode).toBe('09010100101');
      expect(body.schoolName).toBe('Green Valley');
      expect(body.email).toBeNull();
      expect(body.website).toBeNull();
      expect(body.schoolShortName).toBeNull();
      expect(body.schoolGstin).toBe('07AABCU9603R1ZM');
      expect(body.schoolPan).toBe('AABCU9603R');
      expect(body.boardTypeId).toBe(1);
      expect(body.schoolEstablishYear).toBe(1998);
      expect(body.contacts[0]).toMatchObject({ contactName: 'A. Sharma', email: 'a@example.com', mobileNumber: '9876543211', designation: null, alternateMobileNumber: null, isPrimary: true });
      expect(body.contacts[0].contactId).toBeUndefined();
      expect(body).not.toHaveProperty('schoolStatusId');
      expect(body).not.toHaveProperty('logoUrl');
      req.flush(envelope({ school: makeSchool(), username: 'sch2026001', temporaryPassword: 'Tmp#1234', emailSent: false }, 201));
      http.expectNone((r) => r.url.endsWith('/logo'));
    });

    it('shows the one-time credentials panel instead of the form after registering', () => {
      fillValid();
      cmp.submit();
      http.expectOne(`${API}/api/SchoolRegistration/register`).flush(
        envelope({ school: makeSchool(), username: 'sch2026001', temporaryPassword: 'Tmp#1234', emailSent: true, emailSentTo: 'a@example.com' }, 201),
      );
      fixture.detectChanges();
      const el: HTMLElement = fixture.nativeElement;
      expect(el.querySelector('form')).toBeNull();
      expect(el.textContent).toContain('Tmp#1234');
      expect(el.textContent).toContain('sch2026001');
      expect(el.textContent).toMatch(/only once/i);
      expect(el.textContent).toContain('a@example.com');
    });

    it('uploads the picked logo after registration; a failed upload still counts as a successful registration', () => {
      const toast = TestBed.inject(ToastService);
      fillValid();
      cmp.onLogoChanged({ file: new File([new Uint8Array(10)], 'logo.png', { type: 'image/png' }), remove: false });
      cmp.submit();
      http.expectOne(`${API}/api/SchoolRegistration/register`).flush(
        envelope({ school: makeSchool(), username: 'u', temporaryPassword: 'p', emailSent: false }, 201),
      );
      const upload = http.expectOne(`${API}/api/SchoolRegistration/schools/sch2026001/logo`);
      expect(upload.request.body.get('file')).toBeInstanceOf(File);
      upload.flush(envelope(null, 400), { status: 400, statusText: 'Bad Request' });

      expect(cmp.created()).not.toBeNull();
      const messages = toast.toasts();
      expect(messages.some((t) => t.type === 'warning' && /logo/i.test(t.title ?? ''))).toBe(true);
      // the upload failure is reported once, by the form (global toast suppressed for logo calls)
      expect(messages.filter((t) => t.type === 'error')).toHaveLength(0);
    });

    it('keeps the form (and shows the server message) when registration fails', () => {
      const toast = TestBed.inject(ToastService);
      fillValid();
      cmp.submit();
      http
        .expectOne(`${API}/api/SchoolRegistration/register`)
        .flush({ status: false, statusCode: 409, message: 'School code, GSTIN or PAN already exists', data: null }, { status: 409, statusText: 'Conflict' });
      expect(cmp.created()).toBeNull();
      expect(cmp.saving()).toBe(false);
      expect(toast.toasts()[0].message).toBe('School code, GSTIN or PAN already exists');
    });
  });

  describe('edit mode', () => {
    function loadEdit() {
      create('edit', 'sch2026001');
      flushLookups(http);
      http.expectOne(`${API}/api/SchoolRegistration/schools/sch2026001`).flush(envelope(makeSchool()));
      fixture.detectChanges();
    }

    it('loads the saved country/state/district/city chain so every select pre-selects', () => {
      create('edit', 'sch2026001');
      flushLookups(http);
      http.expectOne(`${API}/api/SchoolRegistration/schools/sch2026001`).flush(envelope(makeSchool()));
      // chain requests are issued only after the school is known
      expect(flushLookups(http).sort()).toEqual(['cities/1/2/3', 'districts/1/2', 'states/1']);
      fixture.detectChanges();

      const c = cmp.form.controls;
      expect(c.countryId.value).toBe('1');
      expect(c.stateId.value).toBe('2');
      expect(c.districtId.value).toBe('3');
      expect(c.cityId.value).toBe('4');
      expect(c.stateId.enabled && c.districtId.enabled && c.cityId.enabled).toBe(true);
      expect(cmp.stateOptions().map((o: any) => o.label)).toEqual(['Delhi']);
      expect(cmp.cityOptions().map((o: any) => o.label)).toEqual(['Connaught Place']);

      // DOM check: the <select>s really show the saved values
      const selected = [...(fixture.nativeElement as HTMLElement).querySelectorAll('select')]
        .map((s) => s.selectedOptions[0]?.textContent?.trim())
        .filter(Boolean);
      expect(selected).toEqual(expect.arrayContaining(['India', 'Delhi', 'New Delhi', 'Connaught Place']));
    });

    it('shows the school code read-only and sends an update without schoolCode, keeping contact ids and subscription status', () => {
      loadEdit();
      flushLookups(http);
      fixture.detectChanges();
      const codeInput = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('input[readonly]');
      expect(codeInput?.value).toBe('GVPS-01');

      cmp.form.patchValue({ schoolName: 'Green Valley 2', email: '' });
      cmp.submit();
      const req = http.expectOne((r) => r.method === 'PUT' && r.url === `${API}/api/SchoolRegistration/schools/sch2026001`);
      expect(req.request.body).not.toHaveProperty('schoolCode');
      expect(req.request.body.schoolName).toBe('Green Valley 2');
      expect(req.request.body.email).toBeNull();
      expect(req.request.body.subscriptionStatusId).toBe(7);
      expect(req.request.body.contacts[0]).toMatchObject({ contactId: 11, isPrimary: true });
      req.flush(envelope(makeSchool({ schoolName: 'Green Valley 2' })));
    });

    it('applies a pending logo removal / replacement only when Update is pressed', () => {
      loadEdit();
      flushLookups(http);
      cmp.onLogoChanged({ file: null, remove: true });
      http.expectNone((r) => r.url.includes('/logo'));

      cmp.submit();
      http.expectOne((r) => r.method === 'PUT').flush(envelope(makeSchool()));
      http.expectOne(`${API}/api/SchoolRegistration/schools/sch2026001/logo/remove`).flush(envelope(makeSchool()));
    });

    it('a legacy (non-UDISE) code is shown read-only and never blocks saving', () => {
      loadEdit();
      flushLookups(http);
      expect(cmp.form.controls.schoolCode.value).toBe('GVPS-01');
      expect(cmp.form.controls.schoolCode.disabled).toBe(true);
      expect(cmp.form.valid).toBe(true);
    });

    it('requires a primary contact on edit too: a school with no contacts cannot be saved until one is added', () => {
      create('edit', 'sch2026001');
      flushLookups(http);
      http.expectOne(`${API}/api/SchoolRegistration/schools/sch2026001`).flush(envelope(makeSchool({ contacts: [] })));
      flushLookups(http);
      fixture.detectChanges();
      expect(cmp.contacts.length).toBe(0);

      cmp.submit();
      http.expectNone((r) => r.method === 'PUT');
      expect(cmp.primaryError()).toBe('Add a contact and mark it as the primary contact');

      cmp.addContact();
      cmp.contacts.at(0).patchValue({ contactType: 'Principal', contactName: 'New Principal', email: 'np@example.com', mobileNumber: '9876543219' });
      cmp.submit();
      const req = http.expectOne((r) => r.method === 'PUT');
      expect(req.request.body.contacts).toHaveLength(1);
      expect(req.request.body.contacts[0]).toMatchObject({ contactName: 'New Principal', isPrimary: true });
      req.flush(envelope(makeSchool()));
    });

    it('does not allow removing saved contacts (the API never deletes them)', () => {
      loadEdit();
      flushLookups(http);
      expect(cmp.canRemove(cmp.contacts.at(0))).toBe(false);
      cmp.addContact();
      expect(cmp.canRemove(cmp.contacts.at(1))).toBe(true);
    });

    it('navigates to the list after a successful update', () => {
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      loadEdit();
      flushLookups(http);
      cmp.submit();
      http.expectOne((r) => r.method === 'PUT').flush(envelope(makeSchool()));
      expect(navigate).toHaveBeenCalledWith(['/schools']);
    });
  });

  describe('view mode', () => {
    it('disables the whole form and offers no submit button', () => {
      create('view', 'sch2026001');
      flushLookups(http);
      http.expectOne(`${API}/api/SchoolRegistration/schools/sch2026001`).flush(envelope(makeSchool()));
      flushLookups(http);
      fixture.detectChanges();

      expect(cmp.form.disabled).toBe(true);
      expect(cmp.form.controls.schoolName.value).toBe('Green Valley');
      const el: HTMLElement = fixture.nativeElement;
      expect(el.querySelector('button[type="submit"]')).toBeNull();
      expect(el.textContent).toContain('Edit'); // header action
      expect(el.textContent).not.toContain('Add contact');
    });
  });
});
