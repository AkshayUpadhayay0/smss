import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, TableColumn, TableComponent } from '../../../../shared/components';
import { LeaveApplyModalComponent } from '../../components/leave-apply-modal/leave-apply-modal.component';
import { EmployeeLeave, LeaveRow, LeaveType } from '../../models/leave.model';
import { LeaveService } from '../../services/leave.service';
import { employeeRow, statusTone } from '../../utils/leave-format';

const COLUMNS: TableColumn<LeaveRow>[] = [
  { key: 'leaveTypeName', label: 'Type' },
  { key: 'dates', label: 'Dates' },
  { key: 'days', label: 'Days' },
  { key: 'reason', label: 'Reason' },
  { key: 'status', label: 'Status', type: 'badge', tone: statusTone },
  { key: 'decision', label: 'Decision' },
];

/** The signed-in employee's own leave: apply, and follow each request (a Pending one can be cancelled). */
@Component({
  selector: 'app-my-leave',
  imports: [ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, TableComponent, LeaveApplyModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header title="My Leave" subtitle="Apply for leave and follow your requests" icon="calendar" [breadcrumbs]="breadcrumbs">
      <app-button [disabled]="loadFailed() || types().length === 0" (click)="applying.set(true)"><app-icon name="plus" [size]="16" />Apply for Leave</app-button>
    </app-page-header>

    @if (loadFailed()) {
      <app-card>
        <p class="note" role="alert">
          Your leave requests could not be loaded. Leave is for staff, so this account needs to be linked to an employee record. A School Admin can record leave for you from the Leave Requests page.
        </p>
      </app-card>
    } @else {
      @if (pendingCount() > 0) {
        <p class="summary" role="status">{{ pendingCount() }} {{ pendingCount() === 1 ? 'request is' : 'requests are' }} waiting for a decision.</p>
      }
      <app-card [noPadding]="true">
        <app-table [columns]="columns" [rows]="rows()" [loading]="loading()" [hasActions]="true" emptyMessage="You have not applied for any leave yet.">
          <ng-template #rowActions let-row>
            @if (row.status === 'Pending') {
              <app-button variant="danger-ghost" size="sm" [disabled]="busyId() === row.leaveRequestId" (click)="cancel(row)">Cancel request</app-button>
            }
          </ng-template>
        </app-table>
      </app-card>
    }

    @if (applying()) {
      <app-leave-apply-modal kind="employee" [types]="types()" (saved)="onApplied()" (closed)="applying.set(false)" />
    }
  `,
  styles: `
    .note {
      margin: 0;
      color: var(--color-text-muted);
    }

    .summary {
      margin: 0 0 var(--space-4);
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }
  `,
})
export class MyLeaveComponent implements OnInit {
  private readonly service = inject(LeaveService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'My Leave' }];

  protected readonly requests = signal<EmployeeLeave[]>([]);
  protected readonly types = signal<LeaveType[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busyId = signal<number | null>(null);
  protected readonly applying = signal(false);

  protected readonly rows = computed<LeaveRow[]>(() => this.requests().map(employeeRow));
  protected readonly pendingCount = computed(() => this.requests().filter((r) => r.status === 'Pending').length);

  ngOnInit(): void {
    this.load();
    this.service.types('employee').pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (t) => this.types.set(t), error: () => undefined });
  }

  protected load(): void {
    this.loading.set(true);
    this.service
      .myLeave()
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => {
          this.requests.set(list);
          this.loadFailed.set(false);
        },
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  protected onApplied(): void {
    this.applying.set(false);
    this.load();
  }

  protected async cancel(row: LeaveRow): Promise<void> {
    const confirmed = await this.confirmDialog.confirm({
      title: 'Cancel leave request?',
      message: `Your ${row.leaveTypeName} request for ${row.dates} will be cancelled. You can apply again later.`,
      confirmText: 'Cancel request',
      variant: 'danger',
    });
    if (!confirmed) return;

    this.busyId.set(row.leaveRequestId);
    this.service
      .cancelEmployee(row.leaveRequestId)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: (updated) => {
          this.requests.update((list) => list.map((r) => (r.leaveRequestId === updated.leaveRequestId ? updated : r)));
          this.toast.success('Leave request cancelled.');
        },
        error: () => this.load(), // e.g. it was decided in the meantime: show the truth
      });
  }
}
