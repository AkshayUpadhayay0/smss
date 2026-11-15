import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import {
  Driver,
  DriverListItem,
  DriverLoginCreated,
  DriverLookups,
  DriverPhoto,
  DriverRequest,
  LiveBoard,
  Paged,
  StudentTransport,
  TransportArea,
  TransportAreaRequest,
  Vehicle,
  VehicleLookups,
  VehicleRequest,
} from '../models/transport.model';

export interface ListQuery {
  search?: string;
  /** '' / 'All' = every status. */
  status?: string;
  page: number;
  pageSize: number;
}

/** Drops empty filters; the server filters and pages. */
function params(query: ListQuery, extra: Record<string, string> = {}): Record<string, string | number> {
  const p: Record<string, string | number> = { page: query.page, pageSize: query.pageSize };
  if (query.search?.trim()) p['search'] = query.search.trim();
  if (query.status && query.status !== 'All') p['status'] = query.status;
  for (const [k, v] of Object.entries(extra)) if (v) p[k] = v;
  return p;
}

/** Always "my school's" transport data: the server resolves the school from the token, so no school id is ever sent. */
@Injectable({ providedIn: 'root' })
export class TransportService {
  private readonly api = inject(ApiService);

  // ---- areas
  listAreas(query: ListQuery): Observable<Paged<TransportArea>> {
    return this.api.get<Paged<TransportArea>>('/api/TransportAreas', params(query));
  }
  createArea(request: TransportAreaRequest): Observable<TransportArea> {
    return this.api.post<TransportArea>('/api/TransportAreas', request);
  }
  updateArea(id: number, request: TransportAreaRequest): Observable<TransportArea> {
    return this.api.put<TransportArea>(`/api/TransportAreas/${id}`, request);
  }
  toggleArea(id: number): Observable<TransportArea> {
    return this.api.post<TransportArea>(`/api/TransportAreas/${id}/toggle-status`);
  }

  // ---- vehicles
  vehicleLookups(): Observable<VehicleLookups> {
    return this.api.get<VehicleLookups>('/api/Vehicles/lookups');
  }
  listVehicles(query: ListQuery, vehicleTypeId = ''): Observable<Paged<Vehicle>> {
    return this.api.get<Paged<Vehicle>>('/api/Vehicles', params(query, { vehicleTypeId }));
  }
  createVehicle(request: VehicleRequest): Observable<Vehicle> {
    return this.api.post<Vehicle>('/api/Vehicles', request);
  }
  updateVehicle(id: number, request: VehicleRequest): Observable<Vehicle> {
    return this.api.put<Vehicle>(`/api/Vehicles/${id}`, request);
  }
  toggleVehicle(id: number): Observable<Vehicle> {
    return this.api.post<Vehicle>(`/api/Vehicles/${id}/toggle-status`);
  }

  // ---- drivers
  driverLookups(): Observable<DriverLookups> {
    return this.api.get<DriverLookups>('/api/Drivers/lookups');
  }
  listDrivers(query: ListQuery, vehicleId = ''): Observable<Paged<DriverListItem>> {
    return this.api.get<Paged<DriverListItem>>('/api/Drivers', params(query, { vehicleId }));
  }
  getDriver(id: string): Observable<Driver> {
    return this.api.get<Driver>(`/api/Drivers/${encodeURIComponent(id)}`);
  }
  createDriver(request: DriverRequest): Observable<Driver> {
    return this.api.post<Driver>('/api/Drivers', request);
  }
  updateDriver(id: string, request: DriverRequest): Observable<Driver> {
    return this.api.put<Driver>(`/api/Drivers/${encodeURIComponent(id)}`, request);
  }
  toggleDriver(id: string): Observable<Driver> {
    return this.api.post<Driver>(`/api/Drivers/${encodeURIComponent(id)}/toggle-status`);
  }
  uploadDriverPhoto(id: string, file: File): Observable<DriverPhoto> {
    return this.api.upload<DriverPhoto>(`/api/Drivers/${encodeURIComponent(id)}/photo`, file);
  }
  removeDriverPhoto(id: string): Observable<DriverPhoto> {
    return this.api.post<DriverPhoto>(`/api/Drivers/${encodeURIComponent(id)}/photo/remove`);
  }
  /** Returns the generated credentials ONCE. */
  createDriverLogin(id: string): Observable<DriverLoginCreated> {
    return this.api.post<DriverLoginCreated>(`/api/Drivers/${encodeURIComponent(id)}/create-login`);
  }

  // ---- live map (School Admin and Principal)
  live(): Observable<LiveBoard> {
    return this.api.get<LiveBoard>('/api/Transport/live', undefined, { skipErrorToast: true });
  }

  // ---- a student's transport area (Student profile card)
  getStudentTransport(studentId: string): Observable<StudentTransport> {
    return this.api.get<StudentTransport>(`/api/StudentTransport/${encodeURIComponent(studentId)}`);
  }
  assignStudentTransport(studentId: string, areaId: number): Observable<StudentTransport> {
    return this.api.put<StudentTransport>(`/api/StudentTransport/${encodeURIComponent(studentId)}`, { areaId });
  }
  removeStudentTransport(studentId: string): Observable<StudentTransport> {
    return this.api.post<StudentTransport>(`/api/StudentTransport/${encodeURIComponent(studentId)}/remove`);
  }
}
