import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, shareReplay, throwError } from 'rxjs';
import { SelectOption } from '../../shared/components/select/select.component';
import { BoardType, City, Country, District, RoleLookup, SchoolLevel, SchoolType, State, StatusLookup, StatusType } from '../models/master-data.model';
import { ApiService } from './api.service';

const BASE = '/api/MasterData';

/**
 * Caches lookups for the app session. Failed requests are NOT cached, so a retry hits the server again.
 * Option mappers feed `<app-select [options]>` directly (values are strings, per the select contract).
 */
@Injectable({ providedIn: 'root' })
export class MasterDataService {
  private readonly api = inject(ApiService);
  private readonly cache = new Map<string, Observable<unknown>>();

  private cached<T>(key: string, path: string): Observable<T> {
    let req = this.cache.get(key) as Observable<T> | undefined;
    if (!req) {
      req = this.api.get<T>(`${BASE}/${path}`).pipe(
        catchError((err) => {
          this.cache.delete(key);
          return throwError(() => err);
        }),
        shareReplay(1),
      );
      this.cache.set(key, req);
    }
    return req;
  }

  /** Drop cached lookups (call after a master is created / edited / toggled so dropdowns refresh). */
  invalidate(): void {
    this.cache.clear();
  }

  // ── Location (cascading, cached per parent key) ───────────────────
  getCountries(): Observable<Country[]> {
    return this.cached('countries', 'countries');
  }
  getStates(countryId: number): Observable<State[]> {
    return this.cached(`states:${countryId}`, `states/${countryId}`);
  }
  getDistricts(countryId: number, stateId: number): Observable<District[]> {
    return this.cached(`districts:${countryId}:${stateId}`, `districts/${countryId}/${stateId}`);
  }
  getCities(countryId: number, stateId: number, districtId: number): Observable<City[]> {
    return this.cached(`cities:${countryId}:${stateId}:${districtId}`, `cities/${countryId}/${stateId}/${districtId}`);
  }

  // ── Status / simple lookups ───────────────────────────────────────
  getStatuses(): Observable<StatusLookup[]> {
    return this.cached('status', 'status');
  }
  getStatusesByType(type: StatusType): Observable<StatusLookup[]> {
    return this.getStatuses().pipe(map((list) => list.filter((s) => s.stype === type)));
  }
  getBoardTypes(): Observable<BoardType[]> {
    return this.cached('board-types', 'board-types');
  }
  getSchoolTypes(): Observable<SchoolType[]> {
    return this.cached('school-types', 'school-types');
  }
  getSchoolLevels(): Observable<SchoolLevel[]> {
    return this.cached('school-levels', 'school-levels');
  }
  getRoles(): Observable<RoleLookup[]> {
    return this.cached('role', 'role');
  }

  // ── SelectOption mappers ──────────────────────────────────────────
  getCountryOptions(): Observable<SelectOption[]> {
    return this.getCountries().pipe(map((l) => l.map((c) => ({ label: c.cname, value: c.cid.toString() }))));
  }
  getStateOptions(countryId: number): Observable<SelectOption[]> {
    return this.getStates(countryId).pipe(map((l) => l.map((s) => ({ label: s.sname, value: s.sid.toString() }))));
  }
  getDistrictOptions(countryId: number, stateId: number): Observable<SelectOption[]> {
    return this.getDistricts(countryId, stateId).pipe(map((l) => l.map((d) => ({ label: d.dname, value: d.did.toString() }))));
  }
  getCityOptions(countryId: number, stateId: number, districtId: number): Observable<SelectOption[]> {
    return this.getCities(countryId, stateId, districtId).pipe(map((l) => l.map((c) => ({ label: c.cityName, value: c.cityId.toString() }))));
  }

  /** Inactive entries stay selectable (an existing school may reference one) but are labelled. */
  getBoardTypeOptions(): Observable<SelectOption[]> {
    return this.getBoardTypes().pipe(map((l) => l.map((b) => ({ label: this.label(b.boardName, b.isActive), value: b.boardTypeId.toString() }))));
  }
  getSchoolTypeOptions(): Observable<SelectOption[]> {
    return this.getSchoolTypes().pipe(map((l) => l.map((s) => ({ label: this.label(s.schoolTypeName, s.isActive), value: s.schoolTypeId.toString() }))));
  }
  getSchoolLevelOptions(): Observable<SelectOption[]> {
    return this.getSchoolLevels().pipe(map((l) => l.map((s) => ({ label: this.label(s.schoolLevelName, s.isActive), value: s.schoolLevelId.toString() }))));
  }

  /** General status (Active / Inactive …) — used to display a school's status badge. */
  getGeneralStatusOptions(): Observable<SelectOption[]> {
    return this.getStatusesByType('general status').pipe(map((l) => l.map((s) => ({ label: s.sname, value: s.sid.toString() }))));
  }
  getSubscriptionStatusOptions(): Observable<SelectOption[]> {
    return this.getStatusesByType('school plan status').pipe(map((l) => l.map((s) => ({ label: s.sname, value: s.sid.toString() }))));
  }

  private label(name: string, isActive: boolean): string {
    return isActive ? name : `${name} (Inactive)`;
  }
}
