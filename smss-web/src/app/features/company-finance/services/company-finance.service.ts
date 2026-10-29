import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import {
  CompanyEmployee,
  CompanyEmployeeRequest,
  CompanyExpense,
  CompanyFinanceOverview,
  CompanyPaged,
  CompanySalaryPayment,
  CreateCompanyExpenseRequest,
  ExpenseCategory,
  RecordCompanySalaryRequest,
  RecordStatus,
} from '../models/company-finance.model';

/** SMSS's own staff, expenses, salary payments and financial overview. Super Admin only: the API refuses every other role. */
@Injectable({ providedIn: 'root' })
export class CompanyFinanceService {
  private readonly api = inject(ApiService);

  // ---------------------------------------------------------------- company employees
  employees(search: string, status: 'Active' | 'Inactive' | ''): Observable<CompanyEmployee[]> {
    const params: Record<string, string> = {};
    if (search) params['search'] = search;
    if (status) params['status'] = status;
    return this.api.get<CompanyEmployee[]>('/api/CompanyEmployee', params);
  }

  createEmployee(request: CompanyEmployeeRequest): Observable<CompanyEmployee> {
    return this.api.post<CompanyEmployee>('/api/CompanyEmployee', request);
  }

  updateEmployee(id: number, request: CompanyEmployeeRequest): Observable<CompanyEmployee> {
    return this.api.put<CompanyEmployee>(`/api/CompanyEmployee/${id}`, request);
  }

  /** Active becomes Inactive and back. Nothing is deleted. */
  toggleEmployeeStatus(id: number): Observable<CompanyEmployee> {
    return this.api.post<CompanyEmployee>(`/api/CompanyEmployee/${id}/toggle-status`, {});
  }

  // ---------------------------------------------------------------- expenses
  categories(): Observable<ExpenseCategory[]> {
    return this.api.get<ExpenseCategory[]>('/api/CompanyExpense/categories');
  }

  expenses(categoryId: string, from: string, to: string, status: RecordStatus | '', page: number, pageSize: number): Observable<CompanyPaged<CompanyExpense>> {
    const params: Record<string, string | number> = { page, pageSize };
    if (categoryId) params['categoryId'] = categoryId;
    if (from) params['from'] = from;
    if (to) params['to'] = to;
    if (status) params['status'] = status;
    return this.api.get<CompanyPaged<CompanyExpense>>('/api/CompanyExpense', params);
  }

  createExpense(request: CreateCompanyExpenseRequest): Observable<CompanyExpense> {
    return this.api.post<CompanyExpense>('/api/CompanyExpense', request);
  }

  voidExpense(id: number, voidReason: string): Observable<CompanyExpense> {
    return this.api.post<CompanyExpense>(`/api/CompanyExpense/${id}/void`, { voidReason });
  }

  // ---------------------------------------------------------------- salary payments
  salaryPaymentsOf(companyEmployeeId: number): Observable<CompanyPaged<CompanySalaryPayment>> {
    return this.api.get<CompanyPaged<CompanySalaryPayment>>(`/api/CompanySalary/employee/${companyEmployeeId}`, { pageSize: 100 });
  }

  recordSalary(request: RecordCompanySalaryRequest): Observable<CompanySalaryPayment> {
    return this.api.post<CompanySalaryPayment>('/api/CompanySalary', request);
  }

  voidSalary(id: number, voidReason: string): Observable<CompanySalaryPayment> {
    return this.api.post<CompanySalaryPayment>(`/api/CompanySalary/${id}/void`, { voidReason });
  }

  // ---------------------------------------------------------------- financial overview
  overview(from: string, to: string): Observable<CompanyFinanceOverview> {
    const params: Record<string, string> = {};
    if (from) params['from'] = from;
    if (to) params['to'] = to;
    return this.api.get<CompanyFinanceOverview>('/api/CompanyFinance/overview', params);
  }
}
