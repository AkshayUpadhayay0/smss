import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../core/testing/auth-test-utils';
import { AreaFormModalComponent } from './area-form-modal/area-form-modal.component';
import { DriverFormModalComponent } from './driver-form-modal/driver-form-modal.component';
import { DriverLoginDialogComponent } from './driver-login-dialog/driver-login-dialog.component';
import { VehicleFormModalComponent } from './vehicle-form-modal/vehicle-form-modal.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });

// protected members
type Cmp = { form: any; submit(): void; create?(): void };

describe('Transport dialogs', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  describe('AreaFormModalComponent', () => {
    let fixture: ComponentFixture<AreaFormModalComponent>;
    const open = (area: unknown = null) => {
      fixture = TestBed.createComponent(AreaFormModalComponent);
      fixture.componentRef.setInput('area', area);
      fixture.detectChanges();
    };
    const cmp = () => fixture.componentInstance as unknown as Cmp;

    it('shows the required helper text under the fee field', () => {
      open();
      expect((fixture.nativeElement as HTMLElement).textContent).toContain("Fee amount for this area. How often it is charged follows your school's Transport fee type.");
    });

    it('sends nothing without a name and a fee', () => {
      open();
      cmp().submit();
      http.expectNone(`${API}/api/TransportAreas`);
      expect(cmp().form.controls.areaName.hasError('required')).toBe(true);
      expect(cmp().form.controls.transportFee.hasError('required')).toBe(true);
    });

    it('rejects a negative fee', () => {
      open();
      cmp().form.patchValue({ areaName: 'Civil Lines', transportFee: '-5' });
      cmp().submit();
      http.expectNone(`${API}/api/TransportAreas`);
      expect(cmp().form.controls.transportFee.hasError('fee')).toBe(true);
    });

    it('creates with trimmed text, numbers as numbers and blanks as null', () => {
      open();
      cmp().form.patchValue({ areaName: ' Civil Lines ', distanceKm: '', transportFee: '800', remarks: ' ' });
      cmp().submit();
      const req = http.expectOne(`${API}/api/TransportAreas`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ areaName: 'Civil Lines', distanceKm: null, transportFee: 800, remarks: null });
      req.flush(env({ areaId: 1 }, 201));
    });

    it('edits with PUT to the area id', () => {
      open({ areaId: 7, areaName: 'Old', distanceKm: 6.5, transportFee: 500, remarks: null, statusName: 'Active', activeDriverCount: 0 });
      expect(cmp().form.getRawValue()).toEqual({ areaName: 'Old', distanceKm: '6.5', transportFee: '500', remarks: '' });
      cmp().form.patchValue({ transportFee: '650' });
      cmp().submit();
      const req = http.expectOne(`${API}/api/TransportAreas/7`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ areaName: 'Old', distanceKm: 6.5, transportFee: 650, remarks: null });
      req.flush(env({ areaId: 7 }));
    });
  });

  describe('VehicleFormModalComponent', () => {
    let fixture: ComponentFixture<VehicleFormModalComponent>;
    const LOOKUPS = {
      vehicleTypes: [{ id: 1, name: 'Bus' }, { id: 2, name: 'Van' }],
      ownerships: [{ id: 10, name: 'Owned', isRented: false }, { id: 11, name: 'Rented', isRented: true }],
    };
    const open = (vehicle: unknown = null) => {
      fixture = TestBed.createComponent(VehicleFormModalComponent);
      fixture.componentRef.setInput('vehicle', vehicle);
      fixture.componentRef.setInput('lookups', LOOKUPS);
      fixture.detectChanges();
    };
    const cmp = () => fixture.componentInstance as unknown as Cmp;
    const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';
    const fill = (extra: Record<string, string> = {}) =>
      cmp().form.patchValue({ vehicleLabel: 'Bus 1', registrationNumber: 'up32 ab 1234', vehicleTypeId: '1', ownershipId: '10', ...extra });

    it('lists the types and ownerships it was given', () => {
      open();
      const options = [...(fixture.nativeElement as HTMLElement).querySelectorAll('option')].map((o) => o.textContent?.trim());
      for (const name of ['Bus', 'Van', 'Owned', 'Rented']) expect(options).toContain(name);
    });

    it('shows the owner / vendor fields only for a rented ownership', () => {
      open();
      fill();
      fixture.detectChanges();
      expect(text()).not.toContain('Owner / vendor name');
      cmp().form.patchValue({ ownershipId: '11' });
      fixture.detectChanges();
      expect(text()).toContain('Owner / vendor name');
    });

    it('normalises the plate like the server and never sends owner details for an owned vehicle', () => {
      open();
      fill({ ownerName: 'Left over', ownerContact: '999' });
      cmp().submit();
      const req = http.expectOne(`${API}/api/Vehicles`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toMatchObject({ vehicleLabel: 'Bus 1', registrationNumber: 'UP32AB1234', vehicleTypeId: 1, ownershipId: 10, ownerName: null, ownerContact: null, pucExpiryDate: null });
      req.flush(env({ vehicleId: 1 }, 201));
    });

    it('sends owner details for a rented vehicle', () => {
      open();
      fill({ ownershipId: '11', ownerName: ' Sharma Travels ', ownerContact: '9876543210' });
      cmp().submit();
      const req = http.expectOne(`${API}/api/Vehicles`);
      expect(req.request.body).toMatchObject({ ownershipId: 11, ownerName: 'Sharma Travels', ownerContact: '9876543210' });
      req.flush(env({ vehicleId: 1 }, 201));
    });

    it('refuses a plate with a bad character, a model year out of range and a future service date', () => {
      open();
      fill({ registrationNumber: 'AB@1', modelYear: '1900', seatingCapacity: '500', lastServiceDate: '2999-01-01' });
      cmp().submit();
      http.expectNone(`${API}/api/Vehicles`);
      const c = cmp().form.controls;
      expect([c.registrationNumber.hasError('plate'), c.modelYear.hasError('year'), c.seatingCapacity.hasError('seats'), c.lastServiceDate.hasError('future')]).toEqual([true, true, true, true]);
    });
  });

  describe('DriverFormModalComponent', () => {
    let fixture: ComponentFixture<DriverFormModalComponent>;
    const LOOKUPS = { vehicles: [{ id: 3, name: 'Bus 1 (UP32AB1234)' }], areas: [{ id: 1, name: 'Civil Lines' }, { id: 2, name: 'Ring Road' }] };
    const DRIVER = {
      driverId: 'DPS001DR001', fullName: 'Ramesh Kumar', mobileNumber: '9876543210', email: null, address: '12 Main Road', dlNumber: 'UP32-2020-001',
      dlValidUpto: '2030-01-31', dlStatus: 'OK', dlDaysLeft: 900, vehicleId: 3, vehicleLabel: 'Bus 1', vehiclePlate: 'UP32AB1234',
      areas: [
        { areaId: 1, areaName: 'Civil Lines', isActive: true, morningPickupTime: '07:30', afternoonDropTime: '14:15' },
        { areaId: 9, areaName: 'Old Colony', isActive: false, morningPickupTime: '08:00', afternoonDropTime: null },
      ],
      statusName: 'Active', photoUrl: null, hasLogin: false, loginId: null,
    };
    const open = (driverId: string | null = null) => {
      fixture = TestBed.createComponent(DriverFormModalComponent);
      fixture.componentRef.setInput('driverId', driverId);
      fixture.componentRef.setInput('lookups', LOOKUPS);
      fixture.detectChanges();
    };
    const cmp = () => fixture.componentInstance as unknown as Cmp;

    it('creates: numbers for the ids, the licence normalised, blank email as null', () => {
      open();
      cmp().form.patchValue({
        fullName: ' Ramesh Kumar ', mobileNumber: '9876543210', address: '12 Main Road', dlNumber: 'up32 2020 001', dlValidUpto: '2030-01-31',
        vehicleId: '3', areaIds: ['1', '2'],
      });
      cmp().submit();
      const req = http.expectOne(`${API}/api/Drivers`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({
        fullName: 'Ramesh Kumar', mobileNumber: '9876543210', email: null, address: '12 Main Road', dlNumber: 'UP322020001', dlValidUpto: '2030-01-31',
        vehicleId: 3,
        areas: [
          { areaId: 1, morningPickupTime: null, afternoonDropTime: null },
          { areaId: 2, morningPickupTime: null, afternoonDropTime: null },
        ],
      });
      req.flush(env({ driverId: 'DPS001DR001' }, 201));
    });

    it('refuses a bad mobile number and a bad licence', () => {
      open();
      cmp().form.patchValue({ fullName: 'A', mobileNumber: '12345', address: 'x', dlNumber: 'ab', dlValidUpto: '2030-01-31' });
      cmp().submit();
      http.expectNone(`${API}/api/Drivers`);
      expect([cmp().form.controls.mobileNumber.hasError('mobile'), cmp().form.controls.dlNumber.hasError('dl')]).toEqual([true, true]);
    });

    it('edit: loads the driver, keeps only ACTIVE areas, tells the user about the inactive one, and PUTs the replacement set', () => {
      open('DPS001DR001');
      http.expectOne(`${API}/api/Drivers/DPS001DR001`).flush(env(DRIVER));
      fixture.detectChanges();
      expect(cmp().form.getRawValue().areaIds).toEqual(['1']);
      expect((fixture.nativeElement as HTMLElement).textContent).toContain('Old Colony');
      cmp().submit();
      const req = http.expectOne(`${API}/api/Drivers/DPS001DR001`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toMatchObject({ fullName: 'Ramesh Kumar', vehicleId: 3, areas: [{ areaId: 1, morningPickupTime: '07:30', afternoonDropTime: '14:15' }] });
      expect(req.request.body.areaIds).toBeUndefined();   // the old shape is gone
      req.flush(env(DRIVER));
    });

    describe('approximate pickup and drop times', () => {
      const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';
      const fillBase = () =>
        cmp().form.patchValue({ fullName: 'Ramesh Kumar', mobileNumber: '9876543210', address: '12 Main Road', dlNumber: 'UP32-2020-001', dlValidUpto: '2030-01-31' });

      it('shows a pair of inputs per SELECTED area, with the approximate wording and the help text', () => {
        open();
        expect(text()).not.toContain('Approximate pickup');
        cmp().form.patchValue({ areaIds: ['1', '2'] });
        fixture.detectChanges();
        expect(text()).toContain('Approximate time the bus reaches this area. Not a per-student time.');
        expect((text().match(/Approximate pickup/g) ?? []).length).toBe(2);
        expect((text().match(/Approximate drop/g) ?? []).length).toBe(2);
        expect(text()).toContain('Civil Lines');
        expect(text()).toContain('Ring Road');
        expect((fixture.nativeElement as HTMLElement).querySelectorAll('input[type="time"]').length).toBe(4);
      });

      it('sends the typed times as HH:mm and a blank one as null', () => {
        open();
        fillBase();
        cmp().form.patchValue({ areaIds: ['1', '2'] });
        cmp().form.controls.times.patchValue({ '1': { pickup: '07:30', drop: '14:15' }, '2': { pickup: '', drop: '15:00' } });
        cmp().submit();
        const req = http.expectOne(`${API}/api/Drivers`);
        expect(req.request.body.areas).toEqual([
          { areaId: 1, morningPickupTime: '07:30', afternoonDropTime: '14:15' },
          { areaId: 2, morningPickupTime: null, afternoonDropTime: '15:00' },
        ]);
        req.flush(env({ driverId: 'DPS001DR001' }, 201));
      });

      it('refuses a pickup that is not earlier than the drop, naming the problem, and sends nothing', () => {
        open();
        fillBase();
        cmp().form.patchValue({ areaIds: ['1'] });
        cmp().form.controls.times.patchValue({ '1': { pickup: '15:00', drop: '07:30' } });
        cmp().submit();
        fixture.detectChanges();
        http.expectNone(`${API}/api/Drivers`);
        expect(text()).toContain('Pickup must be earlier than drop.');
        cmp().form.controls.times.patchValue({ '1': { pickup: '07:30', drop: '07:30' } });
        cmp().submit();
        http.expectNone(`${API}/api/Drivers`);
        cmp().form.controls.times.patchValue({ '1': { pickup: '07:30', drop: '07:31' } });
        cmp().submit();
        http.expectOne(`${API}/api/Drivers`).flush(env({ driverId: 'DPS001DR001' }, 201));
      });

      it('deselecting an area removes its row and its times; selecting it again starts blank', () => {
        open();
        fillBase();
        cmp().form.patchValue({ areaIds: ['1', '2'] });
        cmp().form.controls.times.patchValue({ '1': { pickup: '07:30', drop: '14:15' }, '2': { pickup: '08:00', drop: '15:00' } });
        cmp().form.patchValue({ areaIds: ['2'] });
        fixture.detectChanges();
        expect(Object.keys(cmp().form.controls.times.controls)).toEqual(['2']);
        expect(Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.time-row__area')).map((e) => e.textContent?.trim())).toEqual(['Ring Road']);
        cmp().form.patchValue({ areaIds: ['1', '2'] });
        expect(cmp().form.controls.times.getRawValue()['1']).toEqual({ pickup: '', drop: '' });
        expect(cmp().form.controls.times.getRawValue()['2']).toEqual({ pickup: '08:00', drop: '15:00' });   // the kept area kept what was typed
      });

      it('edit: shows the saved times, and clearing one sends null for it', () => {
        open('DPS001DR001');
        http.expectOne(`${API}/api/Drivers/DPS001DR001`).flush(env(DRIVER));
        fixture.detectChanges();
        expect(cmp().form.controls.times.getRawValue()).toEqual({ '1': { pickup: '07:30', drop: '14:15' } });   // the inactive area has no row
        cmp().form.controls.times.patchValue({ '1': { pickup: '', drop: '14:15' } });
        cmp().submit();
        const req = http.expectOne(`${API}/api/Drivers/DPS001DR001`);
        expect(req.request.body.areas).toEqual([{ areaId: 1, morningPickupTime: null, afternoonDropTime: '14:15' }]);
        req.flush(env(DRIVER));
      });

      it('an area without times is sent with nulls (the server keeps the link, the times stay empty)', () => {
        open();
        fillBase();
        cmp().form.patchValue({ areaIds: ['1'] });
        cmp().submit();
        const req = http.expectOne(`${API}/api/Drivers`);
        expect(req.request.body.areas).toEqual([{ areaId: 1, morningPickupTime: null, afternoonDropTime: null }]);
        req.flush(env({ driverId: 'DPS001DR001' }, 201));
      });
    });

    it('edit: an assigned vehicle that is no longer offered (inactive) is cleared and the user is told', () => {
      open('DPS001DR001');
      http.expectOne(`${API}/api/Drivers/DPS001DR001`).flush(env({ ...DRIVER, vehicleId: 99, vehicleLabel: 'Old Van' }));
      fixture.detectChanges();
      expect(cmp().form.getRawValue().vehicleId).toBe('');
      expect((fixture.nativeElement as HTMLElement).textContent).toContain('Old Van is inactive');
    });
  });

  describe('DriverLoginDialogComponent', () => {
    let fixture: ComponentFixture<DriverLoginDialogComponent>;
    const el = () => fixture.nativeElement as HTMLElement;
    const cmp = () => fixture.componentInstance as unknown as { create(): void };

    beforeEach(() => {
      fixture = TestBed.createComponent(DriverLoginDialogComponent);
      fixture.componentRef.setInput('driverId', 'DPS001DR001');
      fixture.componentRef.setInput('driverName', 'Ramesh Kumar');
      fixture.detectChanges();
    });

    it('posts with no body to create-login, then shows the credentials once with the email note', () => {
      cmp().create();
      const req = http.expectOne(`${API}/api/Drivers/DPS001DR001/create-login`);
      expect(req.request.method).toBe('POST');
      req.flush(env({ driverId: 'DPS001DR001', displayName: 'Ramesh Kumar', orgUserId: 'DPS001DR001', temporaryPassword: 'Ab3$kLm9#xQw', role: 'Driver', emailSent: false, emailSentTo: null }, 201));
      fixture.detectChanges();
      const text = el().textContent ?? '';
      expect(text).toContain('Copy these credentials now');
      expect(text).toContain('Ab3$kLm9#xQw');
      expect(text).toContain('Driver');
      expect(text).toContain('No email was sent');
    });
  });
});
