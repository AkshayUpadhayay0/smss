import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../core/testing/auth-test-utils';
import { AreaListComponent } from './area-list/area-list.component';
import { DriverListComponent } from './driver-list/driver-list.component';
import { VehicleListComponent } from './vehicle-list/vehicle-list.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const page = <T>(items: T[]) => ({ items, total: items.length, page: 1, pageSize: 10 });

/**
 * Bug: the transport lists (and the staff list) did not cancel the request on screen when the search changed, so a SLOWER, OLDER answer
 * arriving last replaced the newer search result (the box said "newer" while the table still showed the results for "older").
 */
describe('Transport list searches show the answer to the LATEST search only', () => {
  let http: HttpTestingController;
  afterEach(() => {
    http?.verify();
    clearSessions();
  });

  const CASES: { name: string; url: string; component: Type<unknown>; row: (n: string) => unknown }[] = [
    {
      name: 'areas', url: '/api/TransportAreas', component: AreaListComponent,
      row: (n) => ({ areaId: 1, areaName: n, distanceKm: 1, transportFee: 1, remarks: null, statusName: 'Active', activeDriverCount: 0 }),
    },
    {
      name: 'vehicles', url: '/api/Vehicles', component: VehicleListComponent,
      row: (n) => ({
        vehicleId: 1, vehicleLabel: n, registrationNumber: 'UP32AB1234', vehicleTypeId: 1, vehicleTypeName: 'Bus', ownershipId: 1, ownershipName: 'Owned', isRented: false, modelYear: null,
        seatingCapacity: 40, ownerName: null, ownerContact: null, lastServiceDate: null, pucExpiryDate: null, pucStatus: 'OK', pucDaysLeft: null, insuranceExpiryDate: null,
        insuranceStatus: 'OK', insuranceDaysLeft: null, statusName: 'Active', activeDriverCount: 0,
      }),
    },
    {
      name: 'drivers', url: '/api/Drivers', component: DriverListComponent,
      row: (n) => ({
        driverId: 'D1', fullName: n, mobileNumber: '9876543210', dlNumber: 'X', dlValidUpto: '2030-01-31', dlStatus: 'OK', dlDaysLeft: 900, vehicleId: null, vehicleLabel: null,
        vehiclePlate: null, areaNames: [], statusName: 'Active', photoUrl: null, hasLogin: false,
      }),
    },
  ];

  for (const c of CASES) {
    it(`${c.name}: a newer search cancels the older request, and only the newer answer is shown`, () => {
      clearSessions();
      seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
      TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
      http = TestBed.inject(HttpTestingController);
      const f = TestBed.createComponent(c.component) as ComponentFixture<unknown>;
      f.detectChanges();
      const lists = () => http.match((r) => r.url === `${API}${c.url}`);
      lists()[0].flush(env(page([c.row('Initial row')])));
      http.match((r) => r.url !== `${API}${c.url}`).forEach((r) => r.flush(env([]))); // lookups

      const inst = f.componentInstance as unknown as { load: () => void; searchControl: { setValue: (v: string) => void } };
      inst.searchControl.setValue('older');
      inst.load();
      const [older] = lists();
      inst.searchControl.setValue('newer');
      inst.load();
      const requests: TestRequest[] = lists();
      const newer = requests[requests.length - 1];

      expect(older.cancelled).toBe(true); // the older request was abandoned, not left to land late
      expect(newer.cancelled).toBe(false);
      expect(newer.request.params.get('search')).toBe('newer');
      newer.flush(env(page([c.row('Newer result')])));
      f.detectChanges();
      expect((f.nativeElement as HTMLElement).textContent).toContain('Newer result');
    });
  }
});
