import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import {
  ActivityArea,
  GradeScale,
  GradeScaleListItem,
  SaveActivityAreaRequest,
  SaveCellRequest,
  SaveGradeScaleRequest,
  SaveSchemeRequest,
  SaveSchemeSubjectRequest,
  SaveScheduleRequest,
  SaveSubjectSkillsRequest,
  SaveTermRequest,
  ScalePreset,
  Scheme,
  SchemeFromPresetRequest,
  SchemeListItem,
  SchemePreset,
  ScheduleScheme,
  ReportTemplate,
  StarterAreasResult,
} from '../models/examinations.model';

/** Examination / Report Card setup. The school always comes from the token: no school id is ever sent. */
@Injectable({ providedIn: 'root' })
export class ExaminationsService {
  private readonly api = inject(ApiService);

  // ---- grade scales
  scales(includeInactive = false): Observable<GradeScaleListItem[]> {
    return this.api.get<GradeScaleListItem[]>('/api/GradeScale', includeInactive ? { includeInactive: true } : undefined).pipe(map((l) => l ?? []));
  }
  scale(id: number): Observable<GradeScale> { return this.api.get<GradeScale>(`/api/GradeScale/${id}`); }
  scalePresets(): Observable<ScalePreset[]> { return this.api.get<ScalePreset[]>('/api/GradeScale/presets').pipe(map((l) => l ?? [])); }
  createScale(r: SaveGradeScaleRequest): Observable<GradeScale> { return this.api.post<GradeScale>('/api/GradeScale', r); }
  updateScale(id: number, r: SaveGradeScaleRequest): Observable<GradeScale> { return this.api.put<GradeScale>(`/api/GradeScale/${id}`, r); }
  scaleFromPreset(presetCode: string, name?: string): Observable<GradeScale> { return this.api.post<GradeScale>('/api/GradeScale/from-preset', { presetCode, name: name || null }); }
  deleteScale(id: number): Observable<GradeScale> { return this.api.delete<GradeScale>(`/api/GradeScale/${id}`); }

  // ---- schemes
  schemes(includeInactive = false): Observable<SchemeListItem[]> {
    return this.api.get<SchemeListItem[]>('/api/ReportScheme', includeInactive ? { includeInactive: true } : undefined).pipe(map((l) => l ?? []));
  }
  scheme(id: number): Observable<Scheme> { return this.api.get<Scheme>(`/api/ReportScheme/${id}`); }
  schemePresets(): Observable<SchemePreset[]> { return this.api.get<SchemePreset[]>('/api/ReportScheme/presets').pipe(map((l) => l ?? [])); }
  templates(): Observable<ReportTemplate[]> { return this.api.get<ReportTemplate[]>('/api/ReportScheme/templates').pipe(map((l) => l ?? [])); }
  createScheme(r: SaveSchemeRequest): Observable<Scheme> { return this.api.post<Scheme>('/api/ReportScheme', r); }
  updateScheme(id: number, r: SaveSchemeRequest): Observable<Scheme> { return this.api.put<Scheme>(`/api/ReportScheme/${id}`, r); }
  deleteScheme(id: number): Observable<Scheme> { return this.api.delete<Scheme>(`/api/ReportScheme/${id}`); }
  cloneScheme(id: number, name?: string | null, academicYearId?: number | null): Observable<Scheme> {
    return this.api.post<Scheme>(`/api/ReportScheme/${id}/clone`, { name: name || null, academicYearId: academicYearId ?? null });
  }
  fromPreset(r: SchemeFromPresetRequest): Observable<Scheme> { return this.api.post<Scheme>('/api/ReportScheme/from-preset', r); }
  saveClasses(id: number, classIds: number[]): Observable<Scheme> { return this.api.put<Scheme>(`/api/ReportScheme/${id}/classes`, { classIds }); }
  saveTerms(id: number, terms: SaveTermRequest[]): Observable<Scheme> { return this.api.put<Scheme>(`/api/ReportScheme/${id}/terms`, { terms }); }
  saveSubjects(id: number, subjects: SaveSchemeSubjectRequest[]): Observable<Scheme> { return this.api.put<Scheme>(`/api/ReportScheme/${id}/subjects`, { subjects }); }
  saveMatrix(id: number, cells: SaveCellRequest[]): Observable<Scheme> { return this.api.put<Scheme>(`/api/ReportScheme/${id}/matrix`, { cells }); }
  saveSkills(id: number, subjects: SaveSubjectSkillsRequest[]): Observable<Scheme> { return this.api.put<Scheme>(`/api/ReportScheme/${id}/skills`, { subjects }); }

  // ---- activity areas
  areas(): Observable<ActivityArea[]> { return this.api.get<ActivityArea[]>('/api/ReportActivityArea').pipe(map((l) => l ?? [])); }
  createArea(r: SaveActivityAreaRequest): Observable<ActivityArea> { return this.api.post<ActivityArea>('/api/ReportActivityArea', r); }
  updateArea(id: number, r: SaveActivityAreaRequest): Observable<ActivityArea> { return this.api.put<ActivityArea>(`/api/ReportActivityArea/${id}`, r); }
  deleteArea(id: number): Observable<ActivityArea> { return this.api.delete<ActivityArea>(`/api/ReportActivityArea/${id}`); }
  starterAreas(): Observable<StarterAreasResult> { return this.api.post<StarterAreasResult>('/api/ReportActivityArea/starter'); }

  // ---- exam schedule
  schedule(): Observable<ScheduleScheme[]> { return this.api.get<ScheduleScheme[]>('/api/ReportSchedule').pipe(map((l) => l ?? [])); }
  saveSchedule(r: SaveScheduleRequest): Observable<ScheduleScheme> { return this.api.put<ScheduleScheme>('/api/ReportSchedule', r); }
}
