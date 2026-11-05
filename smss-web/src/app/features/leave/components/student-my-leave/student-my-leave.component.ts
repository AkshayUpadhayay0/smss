import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, TableColumn, TableComponent } from '../../../../shared/components';
import { LeaveRow, LeaveType, StudentLeave } from '../../models/leave.model';
import { LeaveService } from '../../services/leave.service';
import { statusTone, studentRow } from '../../utils/leave-format';
import { LeaveApplyModalComponent } from '../leave-apply-modal/leave-apply-modal.component';

const COLUMNS: TableColumn<LeaveRow>[] = [
  { key: 'leaveTypeName', label: 'Type' },
  { key: 'dates', label: 'Dates' },
  { key: 'days', label: 'Days' },
  { key: 'reason', label: 'Reason' },
  { key: 'status', label: 'Status', type: 'badge', tone: statusTone },
  { key: 'decision', label: 'Decision' },
];

/**
 * A Student's own leave: their requests (every status, newest first), a "New Leave Request" button, and cancel while a request is still Pending.
 * Used by the My Leave page AND the Leave tab of My Profile, so there is one implementation. The server takes the student from the token.
 */
@Component({
  selector: 'app-student-my-leave',
  imports: [ButtonComponent, CardComponent, IconComponent, TableComponent, LeaveApplyModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="bar">
      @if (pendingCount() > 0) {
        <p class="summary" role="status">{{ pendingCount() }} {{ pendingCount() === 1 ? 'request is' : 'requests are' }} waiting for your class teacher.</p>
      } @else {
        <span></span>
      }
      <app-button [disabled]="loadFailed() || types().length === 0" (click)="applying.set(true)"><app-icon name="plus" [size]="16" />New Leave Request</app-button>
    </div>

    @if (loadFailed()) {
      <app-card><p class="note" role="alert">Your leave requests could not be loaded.</p></app-card>
    } @else {
      <app-card [noPadding]="true">
        <app-table [columns]="columns" [rows]="rows()" [loading]="loading()" [hasActions]="true" emptyMessage="You have not asked for any leave yet.">
          <ng-template #rowActions let-row>
            @if (row.status === 'Pending') {
              <app-button variant="danger-ghost" size="sm" [disabled]="busyId() === row.leaveRequestId" (click)="cancel(row)">Cancel request</app-button>
            }
          </ng-template>
        </app-table>
      </app-card>
    }

    @if (applying()) {
      <app-leave-apply-modal kind="self-student" [types]="types()" (saved)="onApplied()" (closed)="applying.set(false)" />
    }
  `,
  styles: `
    :host {
      display: block;
    }

    .bar {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-4);
      flex-wrap: wrap;
      margin-bottom: var(--space-4);
    }

    .note {
      margin: 0;
      color: var(--color-text-muted);
    }

    .summary {
      margin: 0;
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }
  `,
})
export class StudentMyLeaveComponent implements OnInit {
  private readonly service = inject(LeaveService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly columns = COLUMNS;
  protected readonly requests = signal<StudentLeave[]>([]);
  protected readonly types = signal<LeaveType[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busyId = signal<number | null>(null);
  protected readonly applying = signal(false);

  /** The server answers newest first; sorted again here so the order never depends on it. */
  protected readonly rows = computed<LeaveRow[]>(() => [...this.requests()].sort((a, b) => b.leaveRequestId - a.leaveRequestId).map(studentRow));
  protected readonly pendingCount = computed(() => this.requests().filter((r) => r.status === 'Pending').length);

  ngOnInit(): void {
    this.load();
    this.service.types('student').pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (t) => this.types.set(t), error: () => undefined });
  }

  protected load(): void {
    this.loading.set(true);
    this.service
      .myStudentLeave()
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
      message: `Your ${row.leaveTypeName} request for ${row.dates} will be cancelled. You can ask again later.`,
      confirmText: 'Cancel request',
      variant: 'danger',
    });
    if (!confirmed) return;

    this.busyId.set(row.leaveRequestId);
    this.service
      .cancelMyStudent(row.leaveRequestId)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: (updated) => {
          this.requests.update((list) => list.map((r) => (r.leaveRequestId === updated.leaveRequestId ? updated : r)));
          this.toast.success('Leave request cancelled.');
        },
        error: () => this.load(), // e.g. the teacher decided it in the meantime: show the truth
      });
  }
}
