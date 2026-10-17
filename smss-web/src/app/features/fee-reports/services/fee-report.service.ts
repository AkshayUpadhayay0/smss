import { Injectable, inject } from '@angular/core';
import { Observable, forkJoin, catchError, of } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { AcademicYear } from '../../school-setup/models/academic-year.model';
import { SchoolClass } from '../../school-setup/models/class.model';
import { Section } from '../../school-setup/models/section.model';
import { AcademicYearService } from '../../school-setup/services/academic-year.service';
import { ClassService } from '../../school-setup/services/class.service';
import { SectionService } from '../../school-setup/services/section.service';
import { ClassFeeReport, FeeDashboard } from '../models/fee-report.model';

/** Always the caller's own school: the server takes it from the token. */
@Injectable({ providedIn: 'root' })
export class FeeReportService {
  private readonly api = inject(ApiService);
  private readonly years = inject(AcademicYearService);
  private readonly classes = inject(ClassService);
  private readonly sections = inject(SectionService);

  dashboard(academicYearId: number, classId: number | null, sectionId: number | null, days: number): Observable<FeeDashboard> {
    const params: Record<string, string | number> = { academicYearId, days };
    if (classId) params['classId'] = classId;
    if (sectionId) params['sectionId'] = sectionId;
    return this.api.get<FeeDashboard>('/api/FeeDashboard', params);
  }

  classReport(classId: number, academicYearId: number, sectionId: number | null): Observable<ClassFeeReport> {
    const params: Record<string, string | number> = { academicYearId };
    if (sectionId) params['sectionId'] = sectionId;
    return this.api.get<ClassFeeReport>(`/api/FeeReports/class/${classId}`, params);
  }

  /** The year / class / section choices both screens filter by. */
  filterOptions(): Observable<{ years: AcademicYear[]; classes: SchoolClass[]; sections: Section[] }> {
    return forkJoin({
      years: this.years.list().pipe(catchError(() => of([] as AcademicYear[]))),
      classes: this.classes.list().pipe(catchError(() => of([] as SchoolClass[]))),
      sections: this.sections.list().pipe(catchError(() => of([] as Section[]))),
    });
  }
}
