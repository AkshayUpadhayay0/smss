import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, SelectComponent, SelectOption, TableColumn, TableComponent } from '../../../../shared/components';
import { MarkableSection } from '../../../attendance/models/attendance.model';
import { AttendanceService } from '../../../attendance/services/attendance.service';
import { LeaveApplyModalComponent } from '../../components/leave-apply-modal/leave-apply-modal.component';
import { LeaveRejectModalComponent } from '../../components/leave-reject-modal/leave-reject-modal.component';
import { LEAVE_STATUSES, LeaveRow, LeaveStatus, LeaveType, StudentLeave } from '../../models/leave.model';
import { LeaveService } from '../../services/leave.service';
import { statusTone, studentRow } from '../../utils/leave-format';

const COLUMNS: TableColumn<LeaveRow>[] = [
  { key: 'person', label: 'Student' },
  { key: 'group', label: 'Class' },
  { key: 'leaveTypeName', label: 'Type' },
  { key: 'dates', label: 'Dates' },
  { key: 'days', label: 'Days' },
  { key: 'reason', label: 'Reason' },
  { key: 'status', label: 'Status', type: 'badge', tone: statusTone },
  { key: 'decision', label: 'Decision' },
];

/**
 * Student leave for a class teacher (their own section) or a School Admin (any section): record a request on a student's behalf, then approve /
 * reject / cancel it. There is no student-facing screen. The server enforces who may act on which student.
 */
@Component({
  selector: 'app-student-leave',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, SelectComponent, TableComponent, LeaveApplyModalComponent, LeaveRejectModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './student-leave.component.scss',
  template: `
    <app-page-header title="Student Leave" subtitle="Record, approve or reject leave for students of your class" icon="calendar" [breadcrumbs]="breadcrumbs">
      <app-button [disabled]="!sectionId() || types().length === 0" (click)="openRecord()"><app-icon name="plus" [size]="16" />Record Leave</app-button>
    </app-page-header>

    @if (sectionsFailed()) {
      <app-card><p class="note" role="alert">Your sections could not be loaded.</p></app-card>
    } @else if (!loadingSections() && sections().length === 0) {
      <app-card>
        <p class="note">You are not the class teacher of any section{{ yearName() ? ' for ' + yearName() : '' }}, so there is no student leave to manage.</p>
      </app-card>
    } @else {
      <app-card>
        <div class="filters">
          <app-select label="Section" [options]="sectionOptions()" [formControl]="sectionControl" />
          <app-select label="Status" [options]="statusOptions" [formControl]="statusControl" />
          <p class="hint">{{ total() }} {{ total() === 1 ? 'request' : 'requests' }}{{ pendingTotal() ? ' · ' + pendingTotal() + ' pending' : '' }}</p>
        </div>
      </app-card>

      <app-card [noPadding]="true">
        <app-table [columns]="columns" [rows]="rows()" [loading]="loading()" [hasActions]="true" [emptyMessage]="loadFailed() ? 'Could not load leave requests.' : 'No leave requests match.'">
          <ng-template #rowActions let-row>
            @if (row.status === 'Pending') {
              <app-button size="sm" [disabled]="busyId() === row.leaveRequestId" (click)="approve(row)"><app-icon name="check" [size]="14" />Approve</app-button>
              <app-button variant="danger-ghost" size="sm" [disabled]="busyId() === row.leaveRequestId" (click)="rejecting.set(row)"><app-icon name="x" [size]="14" />Reject</app-button>
              <app-button variant="secondary" size="sm" [disabled]="busyId() === row.leaveRequestId" (click)="cancel(row)">Cancel</app-button>
            }
          </ng-template>
        </app-table>
      </app-card>
    }

    @if (recording()) {
      <app-leave-apply-modal kind="student" [people]="students()" [types]="types()" (saved)="onRecorded()" (closed)="recording.set(false)" />
    }
    @if (rejecting(); as r) {
      <app-leave-reject-modal [summary]="r.person + ' · ' + r.leaveTypeName + ' · ' + r.dates" [busy]="busyId() === r.leaveRequestId" (confirmed)="reject(r, $event)" (closed)="rejecting.set(null)" />
    }
  `,
})
export class StudentLeaveComponent implements OnInit {
  private readonly service = inject(LeaveService);
  private readonly attendance = inject(AttendanceService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'Student Leave' }];
  protected readonly statusOptions: SelectOption[] = [{ label: 'All statuses', value: '' }, ...LEAVE_STATUSES.map((s) => ({ label: s, value: s }))];

  protected readonly sections = signal<MarkableSection[]>([]);
  protected readonly yearName = signal('');
  protected readonly loadingSections = signal(true);
  protected readonly sectionsFailed = signal(false);
  protected readonly sectionOptions = computed<SelectOption[]>(() => this.sections().map((s) => ({ label: `${s.className} – ${s.sectionName}`, value: String(s.sectionId) })));
  protected readonly sectionControl = new FormControl('', { nonNullable: true });
  protected readonly statusControl = new FormControl<string>('Pending', { nonNullable: true });
  protected readonly sectionId = signal(0);

  protected readonly requests = signal<StudentLeave[]>([]);
  protected readonly types = signal<LeaveType[]>([]);
  protected readonly students = signal<SelectOption[]>([]);
  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly busyId = signal<number | null>(null);
  protected readonly recording = signal(false);
  protected readonly rejecting = signal<LeaveRow | null>(null);

  protected readonly rows = computed<LeaveRow[]>(() => this.requests().map(studentRow));
  protected readonly total = computed(() => this.requests().length);
  protected readonly pendingTotal = computed(() => this.requests().filter((r) => r.status === 'Pending').length);

  ngOnInit(): void {
    this.service.types('student').pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (t) => this.types.set(t), error: () => undefined });
    this.sectionControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => {
      this.sectionId.set(Number(v) || 0);
      this.load();
    });
    this.statusControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());

    // the sections the caller can act on: a class teacher's own; a School Admin's: every active one
    this.attendance
      .mySections()
      .pipe(finalize(() => this.loadingSections.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (mine) => {
          this.sections.set(mine.sections);
          this.yearName.set(mine.academicYearName);
          if (mine.sections.length > 0) this.sectionControl.setValue(String(mine.sections[0].sectionId)); // emits: loads the queue
        },
        error: () => this.sectionsFailed.set(true), // already toasted by the interceptor
      });
  }

  protected load(): void {
    const section = this.sectionId();
    if (!section) return;
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .studentRequests(section, this.statusControl.value as LeaveStatus | '')
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (list) => this.requests.set(list), error: () => this.loadFailed.set(true) });
  }

  /** The students of the chosen section (today's roster) feed the "Record Leave" picker. */
  protected openRecord(): void {
    this.attendance
      .section(this.sectionId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (s) => {
          this.students.set(s.students.filter((x) => x.onRoster).map((x) => ({ label: `${x.fullName} (${x.studentId})`, value: x.studentId })));
          this.recording.set(true);
        },
        error: () => undefined, // already toasted by the interceptor
      });
  }

  protected onRecorded(): void {
    this.recording.set(false);
    this.load();
  }

  protected async approve(row: LeaveRow): Promise<void> {
    const confirmed = await this.confirmDialog.confirm({
      title: 'Approve leave?',
      message: `${row.person}: ${row.leaveTypeName}, ${row.dates} (${row.days} ${row.days === 1 ? 'day' : 'days'}). Attendance is not changed by this.`,
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
      .decideStudent(row.leaveRequestId, body)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: () => {
          this.toast.success(done);
          if (closeModal) this.rejecting.set(null);
          this.load();
        },
        // The reason (e.g. an overlap with an approved leave) is toasted by the interceptor; reload so the table shows the truth.
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
      .cancelStudent(row.leaveRequestId)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({ next: () => { this.toast.success('Leave request cancelled.'); this.load(); }, error: () => this.load() });
  }
}
