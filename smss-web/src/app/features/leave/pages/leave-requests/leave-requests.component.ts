import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { AuthService } from '../../../../core/services/auth.service';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ROLES } from '../../../../core/config/route-roles';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, SelectComponent, SelectOption, TableColumn, TableComponent } from '../../../../shared/components';
import { EmployeeService } from '../../../employees/services/employee.service';
import { LeaveApplyModalComponent } from '../../components/leave-apply-modal/leave-apply-modal.component';
import { LeaveRejectModalComponent } from '../../components/leave-reject-modal/leave-reject-modal.component';
import { EmployeeLeave, LEAVE_STATUSES, LeaveRow, LeaveStatus, LeaveType } from '../../models/leave.model';
import { LeaveService } from '../../services/leave.service';
import { employeeRow, statusTone } from '../../utils/leave-format';

const COLUMNS: TableColumn<LeaveRow>[] = [
  { key: 'person', label: 'Employee' },
  { key: 'leaveTypeName', label: 'Type' },
  { key: 'dates', label: 'Dates' },
  { key: 'days', label: 'Days' },
  { key: 'reason', label: 'Reason' },
  { key: 'appliedBy', label: 'Applied by' },
  { key: 'status', label: 'Status', type: 'badge', tone: statusTone },
  { key: 'decision', label: 'Decision' },
];

/** School Admin / Principal: employee leave to approve or reject (default view: Pending), plus every request, filterable by status and employee. */
@Component({
  selector: 'app-leave-requests',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, SelectComponent, TableComponent, LeaveApplyModalComponent, LeaveRejectModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './leave-requests.component.scss',
  template: `
    <app-page-header title="Leave Requests" subtitle="Approve or reject staff leave" icon="calendar" [breadcrumbs]="breadcrumbs">
      @if (isSchoolAdmin()) {
        <app-button [disabled]="types().length === 0" (click)="recording.set(true)"><app-icon name="plus" [size]="16" />Record Leave</app-button>
      }
    </app-page-header>

    <app-card>
      <div class="filters">
        <app-select label="Status" [options]="statusOptions" [formControl]="statusControl" />
        <app-select label="Employee" [options]="employeeOptions()" [formControl]="employeeControl" />
        <p class="hint">{{ total() }} {{ total() === 1 ? 'request' : 'requests' }}{{ pendingTotal() ? ' · ' + pendingTotal() + ' pending' : '' }}</p>
      </div>
    </app-card>

    <app-card [noPadding]="true">
      <app-table
        [columns]="columns"
        [rows]="rows()"
        [loading]="loading()"
        [hasActions]="true"
        [emptyMessage]="loadFailed() ? 'Could not load leave requests.' : 'No leave requests match.'"
      >
        <ng-template #rowActions let-row>
          @if (row.status === 'Pending') {
            <app-button size="sm" [disabled]="busyId() === row.leaveRequestId" (click)="approve(row)"><app-icon name="check" [size]="14" />Approve</app-button>
            <app-button variant="danger-ghost" size="sm" [disabled]="busyId() === row.leaveRequestId" (click)="rejecting.set(row)"><app-icon name="x" [size]="14" />Reject</app-button>
            @if (isSchoolAdmin()) {
              <app-button variant="secondary" size="sm" [disabled]="busyId() === row.leaveRequestId" (click)="cancel(row)">Cancel</app-button>
            }
          }
        </ng-template>
      </app-table>
    </app-card>

    @if (recording()) {
      <app-leave-apply-modal kind="employee" [people]="people()" [types]="types()" (saved)="onRecorded()" (closed)="recording.set(false)" />
    }
    @if (rejecting(); as r) {
      <app-leave-reject-modal [summary]="r.person + ' · ' + r.leaveTypeName + ' · ' + r.dates" [busy]="busyId() === r.leaveRequestId" (confirmed)="reject(r, $event)" (closed)="rejecting.set(null)" />
    }
  `,
})
export class LeaveRequestsComponent implements OnInit {
  private readonly service = inject(LeaveService);
  private readonly employees = inject(EmployeeService);
  private readonly auth = inject(AuthService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'Leave Requests' }];
  protected readonly statusOptions: SelectOption[] = [{ label: 'All statuses', value: '' }, ...LEAVE_STATUSES.map((s) => ({ label: s, value: s }))];

  protected readonly isSchoolAdmin = computed(() => this.auth.currentUserRoles().includes(ROLES.SCHOOL_ADMIN));
  protected readonly statusControl = new FormControl<string>('Pending', { nonNullable: true });
  protected readonly employeeControl = new FormControl<string>('', { nonNullable: true });

  protected readonly requests = signal<EmployeeLeave[]>([]);
  protected readonly types = signal<LeaveType[]>([]);
  /** Everyone who has ever had a request (the employee filter), plus - for a School Admin - every active employee to record leave for. */
  private readonly knownPeople = signal<ReadonlyMap<string, string>>(new Map());
  protected readonly people = signal<SelectOption[]>([]);
  protected readonly employeeOptions = computed<SelectOption[]>(() => [
    { label: 'All employees', value: '' },
    ...[...this.knownPeople()].map(([value, label]) => ({ label: `${label} (${value})`, value })).sort((a, b) => a.label.localeCompare(b.label)),
  ]);

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busyId = signal<number | null>(null);
  protected readonly recording = signal(false);
  protected readonly rejecting = signal<LeaveRow | null>(null);

  protected readonly rows = computed<LeaveRow[]>(() => this.requests().map(employeeRow));
  protected readonly total = computed(() => this.requests().length);
  protected readonly pendingTotal = computed(() => this.requests().filter((r) => r.status === 'Pending').length);

  ngOnInit(): void {
    this.statusControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());
    this.employeeControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());
    this.load();
    this.loadPeople();
    this.service.types('employee').pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (t) => this.types.set(t), error: () => undefined });
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .employeeRequests(this.statusControl.value as LeaveStatus | '', this.employeeControl.value)
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => {
          this.requests.set(list);
          // anyone seen in a result can be filtered on, even for a Principal who cannot list employees
          this.knownPeople.update((m) => new Map([...m, ...list.map((r) => [r.employeeId, r.employeeName] as const)]));
        },
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  /** The filter's people come from every request (not just the filtered ones); a School Admin can also pick any active employee to record leave for. */
  private loadPeople(): void {
    this.service
      .employeeRequests()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (all) => this.knownPeople.update((m) => new Map([...m, ...all.map((r) => [r.employeeId, r.employeeName] as const)])), error: () => undefined });
    if (!this.isSchoolAdmin()) return;
    this.employees
      .list({ status: 'Active' }, 1, 100)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => this.people.set(page.items.map((e) => ({ label: `${e.fullName} (${e.employeeId})`, value: e.employeeId }))),
        error: () => undefined,
      });
  }

  protected onRecorded(): void {
    this.recording.set(false);
    this.load();
  }

  protected async approve(row: LeaveRow): Promise<void> {
    const confirmed = await this.confirmDialog.confirm({
      title: 'Approve leave?',
      message: `${row.person}: ${row.leaveTypeName}, ${row.dates} (${row.days} ${row.days === 1 ? 'day' : 'days'}).`,
      confirmText: 'Approve',
      variant: 'primary',
    });
    if (confirmed) this.decide(row, { decision: 'Approve' }, 'Leave approved.');
  }

  protected reject(row: LeaveRow, reason: string): void {
    this.decide(row, { decision: 'Reject', decisionReason: reason }, 'Leave rejected.', true);
  }

  private decide(row: LeaveRow, body: { decision: 'Approve' | 'Reject'; decisionReason?: string }, done: string, closeModal = false): void {
    this.busyId.set(row.leaveRequestId);
    this.service
      .decideEmployee(row.leaveRequestId, body)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: () => {
          this.toast.success(done);
          if (closeModal) this.rejecting.set(null);
          this.load();
        },
        // The reason (e.g. 409 "overlaps an approved Casual Leave ...") is toasted by the interceptor; reload so the table shows the truth.
        error: () => {
          if (closeModal) this.rejecting.set(null);
          this.load();
        },
      });
  }

  protected async cancel(row: LeaveRow): Promise<void> {
    const confirmed = await this.confirmDialog.confirm({
      title: 'Cancel leave request?',
      message: `${row.person}'s ${row.leaveTypeName} request for ${row.dates} will be cancelled.`,
      confirmText: 'Cancel request',
      variant: 'danger',
    });
    if (!confirmed) return;
    this.busyId.set(row.leaveRequestId);
    this.service
      .cancelEmployee(row.leaveRequestId)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({ next: () => { this.toast.success('Leave request cancelled.'); this.load(); }, error: () => this.load() });
  }
}
