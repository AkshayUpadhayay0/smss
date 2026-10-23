import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import {
  ApplyEmployeeLeaveRequest,
  ApplyStudentLeaveRequest,
  DecideLeaveRequest,
  EmployeeLeave,
  LeaveStatus,
  LeaveType,
  StudentLeave,
} from '../models/leave.model';

const TYPES = '/api/LeaveType';
const EMPLOYEE = '/api/EmployeeLeave';
const STUDENT = '/api/StudentLeave';

/** The school and the acting user come from the token: nothing about them is ever sent. */
@Injectable({ providedIn: 'root' })
export class LeaveService {
  private readonly api = inject(ApiService);

  types(appliesTo: 'employee' | 'student'): Observable<LeaveType[]> {
    return this.api.get<LeaveType[]>(TYPES, { appliesTo }).pipe(map((l) => l ?? []));
  }

  // ---- employee leave
  applyEmployee(request: ApplyEmployeeLeaveRequest): Observable<EmployeeLeave> {
    return this.api.post<EmployeeLeave>(EMPLOYEE, request);
  }

  myLeave(): Observable<EmployeeLeave[]> {
    return this.api.get<EmployeeLeave[]>(`${EMPLOYEE}/my`).pipe(map((l) => l ?? []));
  }

  /** School Admin / Principal: every employee request, optionally one status and / or one employee. */
  employeeRequests(status?: LeaveStatus | '', employeeId?: string): Observable<EmployeeLeave[]> {
    const params: Record<string, string> = {};
    if (status) params['status'] = status;
    if (employeeId) params['employeeId'] = employeeId;
    return this.api.get<EmployeeLeave[]>(EMPLOYEE, params).pipe(map((l) => l ?? []));
  }

  decideEmployee(id: number, request: DecideLeaveRequest): Observable<EmployeeLeave> {
    return this.api.put<EmployeeLeave>(`${EMPLOYEE}/${id}/decide`, request);
  }

  cancelEmployee(id: number): Observable<EmployeeLeave> {
    return this.api.post<EmployeeLeave>(`${EMPLOYEE}/${id}/cancel`);
  }

  // ---- student leave
  applyStudent(request: ApplyStudentLeaveRequest): Observable<StudentLeave> {
    return this.api.post<StudentLeave>(STUDENT, request);
  }

  /** A School Admin sees every request (or one section's); a class teacher only their own section(s). */
  studentRequests(sectionId?: number, status?: LeaveStatus | ''): Observable<StudentLeave[]> {
    const params: Record<string, string | number> = {};
    if (sectionId) params['sectionId'] = sectionId;
    if (status) params['status'] = status;
    return this.api.get<StudentLeave[]>(STUDENT, params).pipe(map((l) => l ?? []));
  }

  decideStudent(id: number, request: DecideLeaveRequest): Observable<StudentLeave> {
    return this.api.put<StudentLeave>(`${STUDENT}/${id}/decide`, request);
  }

  cancelStudent(id: number): Observable<StudentLeave> {
    return this.api.post<StudentLeave>(`${STUDENT}/${id}/cancel`);
  }
}
