import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { errorInterceptor } from '../../../core/interceptors/error.interceptor';
import { SchoolsListModel } from '../models/school.model';

export const API = environment.apiUrl;

export function envelope<T>(data: T, statusCode = 200) {
  return { status: true, statusCode, message: 'ok', data };
}

export function setupHttp() {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(withInterceptors([errorInterceptor])), provideHttpClientTesting(), provideRouter([{ path: '**', children: [] }])],
  });
  return TestBed.inject(HttpTestingController);
}

/** Canned master-data responses, keyed by URL suffix. */
const LOOKUPS: Record<string, unknown> = {
  countries: [{ cid: 1, cname: 'India' }, { cid: 9, cname: 'Elsewhere' }],
  'states/1': [{ cid: 1, sid: 2, sname: 'Delhi' }],
  'states/9': [{ cid: 9, sid: 20, sname: 'Other State' }],
  'districts/1/2': [{ cid: 1, sid: 2, did: 3, dname: 'New Delhi' }],
  'cities/1/2/3': [{ cid: 1, sid: 2, did: 3, cityId: 4, cityName: 'Connaught Place' }],
  status: [
    { sid: 1, sname: 'Active', stype: 'general status' },
    { sid: 2, sname: 'Inactive', stype: 'general status' },
    { sid: 7, sname: 'Trial', stype: 'school plan status' },
  ],
  'board-types': [{ boardTypeId: 1, boardCode: 'CBSE', boardName: 'CBSE', isActive: true }],
  'school-types': [{ schoolTypeId: 1, schoolTypeCode: 'PVT', schoolTypeName: 'Private', isActive: true }],
  'school-levels': [{ schoolLevelId: 1, schoolLevelCode: 'SEC', schoolLevelName: 'Secondary', isActive: true }],
};

/** Answers every pending master-data request; returns the URL suffixes it answered. */
export function flushLookups(http: HttpTestingController): string[] {
  const prefix = `${API}/api/MasterData/`;
  const answered: string[] = [];
  for (const req of http.match((r) => r.url.startsWith(prefix))) {
    const key = req.request.url.slice(prefix.length);
    answered.push(key);
    req.flush(envelope(LOOKUPS[key] ?? []));
  }
  return answered;
}

export function makeSchool(overrides: Partial<SchoolsListModel> = {}): SchoolsListModel {
  return {
    schoolId: 'sch2026001',
    schoolCode: 'GVPS-01',
    schoolName: 'Green Valley',
    schoolShortName: null,
    schoolTypeId: 1,
    schoolLevelId: 1,
    boardTypeId: 1,
    schoolEstablishYear: 1998,
    schoolGstin: null,
    schoolPan: null,
    countryId: 1,
    stateId: 2,
    districtId: 3,
    cityId: 4,
    addressLine1: 'Street 1',
    addressLine2: null,
    pincode: '110001',
    email: 'gv@example.com',
    mobileNumber: '9876543210',
    website: null,
    subscriptionPlanId: 5,
    subscriptionStartDate: '2026-01-01',
    subscriptionEndDate: '2026-12-31',
    subscriptionStatusId: 7,
    schoolStatusId: 1,
    logoUrl: null,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    contacts: [
      {
        contactId: 11,
        contactType: 'Principal',
        contactName: 'A. Sharma',
        designation: null,
        email: 'a@example.com',
        mobileNumber: '9876543211',
        alternateMobileNumber: null,
        isPrimary: true,
        statusId: 1,
      },
    ],
    ...overrides,
  };
}
