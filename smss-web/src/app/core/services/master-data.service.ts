import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map, shareReplay } from 'rxjs';
import { environment } from '../../../environments/environment';   // adjust depth if needed
import { SelectOption } from '../../shared/components/select/select.component';
import {
    BoardType,
    City,
    Country,
    District,
    RoleLookup,
    SchoolLevel,
    SchoolType,
    State,
    StatusLookup,
    StatusType,
} from '../models/master-data.model';

interface ApiEnvelope<T> {
    status: boolean;
    statusCode: number;
    message: string;
    data: T;
}

@Injectable({ providedIn: 'root' })
export class MasterDataService {
    private readonly baseUrl = `${environment.apiUrl}/api/MasterData`;

    // Static/rarely-changing lookups — fetched once per app session, then replayed
    private countries$?: Observable<Country[]>;
    private statuses$?: Observable<StatusLookup[]>;
    private boardTypes$?: Observable<BoardType[]>;
    private schoolTypes$?: Observable<SchoolType[]>;
    private schoolLevels$?: Observable<SchoolLevel[]>;
    private roles$?: Observable<RoleLookup[]>;

    // Cascading location lookups — cached per parent key so re-selecting the same
    // country/state/district doesn't refire the HTTP call
    private statesCache = new Map<number, Observable<State[]>>();
    private districtsCache = new Map<string, Observable<District[]>>();
    private citiesCache = new Map<string, Observable<City[]>>();

    constructor(private http: HttpClient) { }

    // ---------------- Location (cascading) ----------------

    getCountries(): Observable<Country[]> {
        if (!this.countries$) {
            this.countries$ = this.http
                .get<ApiEnvelope<Country[]>>(`${this.baseUrl}/countries`)
                .pipe(map((res) => res.data), shareReplay(1));
        }
        return this.countries$;
    }

    getStates(countryId: number): Observable<State[]> {
        if (!this.statesCache.has(countryId)) {
            const req$ = this.http
                .get<ApiEnvelope<State[]>>(`${this.baseUrl}/states/${countryId}`)
                .pipe(map((res) => res.data), shareReplay(1));
            this.statesCache.set(countryId, req$);
        }
        return this.statesCache.get(countryId)!;
    }

    getDistricts(countryId: number, stateId: number): Observable<District[]> {
        const key = `${countryId}:${stateId}`;
        if (!this.districtsCache.has(key)) {
            const req$ = this.http
                .get<ApiEnvelope<District[]>>(`${this.baseUrl}/districts/${countryId}/${stateId}`)
                .pipe(map((res) => res.data), shareReplay(1));
            this.districtsCache.set(key, req$);
        }
        return this.districtsCache.get(key)!;
    }

    getCities(countryId: number, stateId: number, districtId: number): Observable<City[]> {
        const key = `${countryId}:${stateId}:${districtId}`;
        if (!this.citiesCache.has(key)) {
            const req$ = this.http
                .get<ApiEnvelope<City[]>>(`${this.baseUrl}/cities/${countryId}/${stateId}/${districtId}`)
                .pipe(map((res) => res.data), shareReplay(1));
            this.citiesCache.set(key, req$);
        }
        return this.citiesCache.get(key)!;
    }

    // ---------------- Status (filtered by stype) ----------------

    getStatuses(): Observable<StatusLookup[]> {
        if (!this.statuses$) {
            this.statuses$ = this.http
                .get<ApiEnvelope<StatusLookup[]>>(`${this.baseUrl}/status`)
                .pipe(map((res) => res.data), shareReplay(1));
        }
        return this.statuses$;
    }

    getStatusesByType(type: StatusType): Observable<StatusLookup[]> {
        return this.getStatuses().pipe(map((list) => list.filter((s) => s.stype === type)));
    }

    // ---------------- Simple lookups ----------------

    getBoardTypes(): Observable<BoardType[]> {
        if (!this.boardTypes$) {
            this.boardTypes$ = this.http
                .get<ApiEnvelope<BoardType[]>>(`${this.baseUrl}/board-types`)
                .pipe(map((res) => res.data), shareReplay(1));
        }
        return this.boardTypes$;
    }

    getSchoolTypes(): Observable<SchoolType[]> {
        if (!this.schoolTypes$) {
            this.schoolTypes$ = this.http
                .get<ApiEnvelope<SchoolType[]>>(`${this.baseUrl}/school-types`)
                .pipe(map((res) => res.data), shareReplay(1));
        }
        return this.schoolTypes$;
    }

    getSchoolLevels(): Observable<SchoolLevel[]> {
        if (!this.schoolLevels$) {
            this.schoolLevels$ = this.http
                .get<ApiEnvelope<SchoolLevel[]>>(`${this.baseUrl}/school-levels`)
                .pipe(map((res) => res.data), shareReplay(1));
        }
        return this.schoolLevels$;
    }

    getRoles(): Observable<RoleLookup[]> {
        if (!this.roles$) {
            this.roles$ = this.http
                .get<ApiEnvelope<RoleLookup[]>>(`${this.baseUrl}/role`)
                .pipe(map((res) => res.data), shareReplay(1));
        }
        return this.roles$;
    }

    // ---------------- SelectOption convenience mappers ----------------
    // Feed these straight into [options] on <app-select>

    getCountryOptions(): Observable<SelectOption[]> {
        return this.getCountries().pipe(
            map((list) => list.map((c) => ({ label: c.cname, value: c.cid.toString() }))),
        );
    }

    getStateOptions(countryId: number): Observable<SelectOption[]> {
        return this.getStates(countryId).pipe(
            map((list) => list.map((s) => ({ label: s.sname, value: s.sid.toString() }))),
        );
    }

    getDistrictOptions(countryId: number, stateId: number): Observable<SelectOption[]> {
        return this.getDistricts(countryId, stateId).pipe(
            map((list) => list.map((d) => ({ label: d.dname, value: d.did.toString() }))),
        );
    }

    getCityOptions(countryId: number, stateId: number, districtId: number): Observable<SelectOption[]> {
        return this.getCities(countryId, stateId, districtId).pipe(
            map((list) => list.map((c) => ({ label: c.cityName, value: c.cityId.toString() }))),
        );
    }

    getBoardTypeOptions(): Observable<SelectOption[]> {
        return this.getBoardTypes().pipe(
            map((list) => list.map((b) => ({ label: b.boardName, value: b.boardTypeId.toString() }))),
        );
    }

    getSchoolTypeOptions(): Observable<SelectOption[]> {
        return this.getSchoolTypes().pipe(
            map((list) => list.map((s) => ({ label: s.schoolTypeName, value: s.schoolTypeId.toString() }))),
        );
    }

    getSchoolLevelOptions(): Observable<SelectOption[]> {
        return this.getSchoolLevels().pipe(
            map((list) => list.map((s) => ({ label: s.schoolLevelName, value: s.schoolLevelId.toString() }))),
        );
    }

    getSchoolStatusOptions(): Observable<SelectOption[]> {
        return this.getStatusesByType('general status').pipe(
            map((list) => list.map((s) => ({ label: s.sname, value: s.sid.toString() }))),
        );
    }

    getSubscriptionStatusOptions(): Observable<SelectOption[]> {
        return this.getStatusesByType('school plan status').pipe(
            map((list) => list.map((s) => ({ label: s.sname, value: s.sid.toString() }))),
        );
    }
    
}