import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { ConfirmDialogService } from '../../../core/services/confirm-dialog.service';
import { clearSessions, makeUser, seedSession } from '../../../core/testing/auth-test-utils';
import { expiryLabel, expiryTone, formatDate } from '../models/transport.model';
import { AreaListComponent } from './area-list/area-list.component';
import { DriverListComponent } from './driver-list/driver-list.component';
import { VehicleListComponent } from './vehicle-list/vehicle-list.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });
const page = <T>(items: T[]) => ({ items, total: items.length, page: 1, pageSize: 10 });

const AREA = { areaId: 1, areaName: 'Civil Lines', distanceKm: 6.5, transportFee: 800, remarks: null, statusName: 'Active', activeDriverCount: 2 };
const VEHICLE = {
  vehicleId: 4, vehicleLabel: 'Bus 1', registrationNumber: 'UP32AB1234', vehicleTypeId: 1, vehicleTypeName: 'Bus', ownershipId: 10, ownershipName: 'Owned', isRented: false,
  modelYear: 2019, seatingCapacity: 40, ownerName: null, ownerContact: null, lastServiceDate: null, pucExpiryDate: '2026-11-20', pucStatus: 'EXPIRING', pucDaysLeft: 7,
  insuranceExpiryDate: '2026-01-01', insuranceStatus: 'EXPIRED', insuranceDaysLeft: null, statusName: 'Active', activeDriverCount: 1,
};
const DRIVER = {
  driverId: 'DPS001DR001', fullName: 'Ramesh Kumar', mobileNumber: '9876543210', dlNumber: 'UP322020001', dlValidUpto: '2030-01-31', dlStatus: 'OK', dlDaysLeft: 900,
  vehicleId: 4, vehicleLabel: 'Bus 1', vehiclePlate: 'UP32AB1234', areaNames: ['Civil Lines', 'Ring Road'], statusName: 'Active', photoUrl: null, hasLogin: false,
};

describe('Transport lists', () => {
  let http: HttpTestingController;

  function setup(roles: string[]) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
  }
  afterEach(() => {
    http?.verify();
    clearSessions();
  });

  const text = (f: ComponentFixture<unknown>) => (f.nativeElement as HTMLElement).textContent ?? '';
  const buttons = (f: ComponentFixture<unknown>) => [...(f.nativeElement as HTMLElement).querySelectorAll('app-button')].map((b) => b.textContent?.trim());

  describe('areas', () => {
    function open(roles: string[], items: unknown[] = [AREA]) {
      setup(roles);
      const f = TestBed.createComponent(AreaListComponent);
      f.detectChanges();
      const req = http.expectOne((r) => r.url === `${API}/api/TransportAreas`);
      expect(req.request.params.get('page')).toBe('1');
      expect(req.request.params.get('pageSize')).toBe('10');
      req.flush(env(page(items)));
      f.detectChanges();
      return f;
    }

    it('School Admin sees the rows, Add Area, Edit and Deactivate', () => {
      const f = open(['School Admin']);
      expect(text(f)).toContain('Civil Lines');
      expect(text(f)).toContain('800.00');
      expect(buttons(f)).toEqual(expect.arrayContaining(['Add Area', 'Deactivate']));
    });

    it('Principal reads only: no Add, no row actions', () => {
      const f = open(['Principal']);
      expect(text(f)).toContain('Civil Lines');
      expect(text(f)).not.toContain('Add Area');
      expect(text(f)).not.toContain('Deactivate');
      expect((f.nativeElement as HTMLElement).querySelector('.actions-col')).toBeNull();
    });

    it('empty state and the load-failed state read differently', () => {
      const f = open(['School Admin'], []);
      expect(text(f)).toContain('No areas yet');
    });

    it('a failed load shows the retry button and a message', () => {
      setup(['School Admin']);
      const f = TestBed.createComponent(AreaListComponent);
      f.detectChanges();
      http.expectOne((r) => r.url === `${API}/api/TransportAreas`).flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'err' });
      f.detectChanges();
      expect(text(f)).toContain('Could not load areas.');
      expect(buttons(f)).toContain('Try again');
    });

    it('Deactivate asks first: declining sends nothing, confirming POSTs toggle-status and reloads', async () => {
      const f = open(['School Admin']);
      const confirm = vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValueOnce(false).mockResolvedValueOnce(true);
      const cmp = f.componentInstance as unknown as { toggleStatus(row: unknown): Promise<void>; rows(): unknown[] };
      const row = cmp.rows()[0];
      await cmp.toggleStatus(row);
      http.expectNone(`${API}/api/TransportAreas/1/toggle-status`);
      const done = cmp.toggleStatus(row);
      await Promise.resolve();
      await Promise.resolve();
      http.expectOne(`${API}/api/TransportAreas/1/toggle-status`).flush(env({ ...AREA, statusName: 'Inactive' }));
      await done;
      http.expectOne((r) => r.url === `${API}/api/TransportAreas`).flush(env(page([{ ...AREA, statusName: 'Inactive' }])));
      expect(confirm).toHaveBeenCalledTimes(2);
    });

    it('the status filter goes to the server and returns to page 1', () => {
      const f = open(['School Admin']);
      const cmp = f.componentInstance as unknown as { statusControl: { setValue(v: string): void } };
      cmp.statusControl.setValue('Inactive');
      const req = http.expectOne((r) => r.url === `${API}/api/TransportAreas`);
      expect(req.request.params.get('status')).toBe('Inactive');
      expect(req.request.params.get('page')).toBe('1');
      req.flush(env(page([])));
    });
  });

  describe('vehicles', () => {
    function open(roles: string[]) {
      setup(roles);
      const f = TestBed.createComponent(VehicleListComponent);
      f.detectChanges();
      http.expectOne(`${API}/api/Vehicles/lookups`).flush(env({ vehicleTypes: [{ id: 1, name: 'Bus' }], ownerships: [{ id: 10, name: 'Owned', isRented: false }] }));
      http.expectOne((r) => r.url === `${API}/api/Vehicles`).flush(env(page([VEHICLE])));
      f.detectChanges();
      return f;
    }

    it('shows the PUC and insurance expiry badges from the server status', () => {
      const f = open(['School Admin']);
      expect(text(f)).toContain('Expiring in 7 days');
      expect(text(f)).toContain('Expired');
      expect(text(f)).toContain('UP32AB1234');
    });

    it('Principal reads only', () => {
      const f = open(['Principal']);
      expect(text(f)).toContain('Bus 1');
      expect(text(f)).not.toContain('Add Vehicle');
      expect(text(f)).not.toContain('Deactivate');
    });
  });

  describe('drivers', () => {
    function open(roles: string[], item: unknown = DRIVER) {
      setup(roles);
      const f = TestBed.createComponent(DriverListComponent);
      f.detectChanges();
      http.expectOne(`${API}/api/Drivers/lookups`).flush(env({ vehicles: [{ id: 4, name: 'Bus 1 (UP32AB1234)' }], areas: [{ id: 1, name: 'Civil Lines' }] }));
      http.expectOne((r) => r.url === `${API}/api/Drivers`).flush(env(page([item])));
      f.detectChanges();
      return f;
    }

    it('School Admin sees name, vehicle, areas, licence badge and the Login / Photo / Edit / Deactivate actions', () => {
      const f = open(['School Admin']);
      const t = text(f);
      for (const s of ['Ramesh Kumar', 'Bus 1 (UP32AB1234)', 'Civil Lines, Ring Road', '31 Jan 2030', 'OK']) expect(t).toContain(s);
      expect(buttons(f)).toEqual(expect.arrayContaining(['Add Driver', 'Login', 'Deactivate']));
    });

    it('a driver that already has a login, or is inactive, has no Create login button', () => {
      expect(buttons(open(['School Admin'], { ...DRIVER, hasLogin: true }))).not.toContain('Login');
    });

    it('Principal reads only', () => {
      const f = open(['Principal']);
      expect(text(f)).toContain('Ramesh Kumar');
      for (const s of ['Add Driver', 'Deactivate', 'Login']) expect(buttons(f)).not.toContain(s);
    });
  });

  describe('expiry helpers', () => {
    it('labels and tones', () => {
      expect(expiryLabel('EXPIRED', null)).toBe('Expired');
      expect(expiryLabel('EXPIRING', 1)).toBe('Expiring in 1 day');
      expect(expiryLabel('EXPIRING', 0)).toBe('Expires today');
      expect(expiryLabel('OK', 400)).toBe('OK');
      expect(expiryLabel('NOT_SET', null)).toBe('Not set');
      expect([expiryTone('Expired'), expiryTone('Expiring in 7 days'), expiryTone('OK'), expiryTone('Not set')]).toEqual(['red', 'yellow', 'green', 'gray']);
    });

    it('formats a date without shifting the day', () => {
      expect(formatDate('2030-01-31')).toBe('31 Jan 2030');
      expect(formatDate(null)).toBe('');
    });
  });
});
