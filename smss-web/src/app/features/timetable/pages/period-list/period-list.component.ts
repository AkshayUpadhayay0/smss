import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, TableColumn, TableComponent } from '../../../../shared/components';
import { PeriodFormModalComponent } from '../../components/period-form-modal/period-form-modal.component';
import { shortTime } from '../../components/timetable-grid/timetable-grid.component';
import { Period } from '../../models/timetable.model';
import { TimetableService } from '../../services/timetable.service';

interface PeriodRow extends Record<string, unknown> {
  periodId: number;
  periodNumber: number;
  time: string;
  usage: string;
  statusName: string;
  isActive: boolean;
}

const COLUMNS: TableColumn<PeriodRow>[] = [
  { key: 'periodNumber', label: 'Period' },
  { key: 'time', label: 'Time' },
  { key: 'usage', label: 'Used in timetables' },
  { key: 'statusName', label: 'Status', type: 'status' },
];

/** The school's bell schedule: the periods every section's timetable is laid out in. */
@Component({
  selector: 'app-period-list',
  imports: [ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, TableComponent, PeriodFormModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header title="Periods" subtitle="The school's bell schedule: every timetable is laid out in these periods" icon="calendar" [breadcrumbs]="breadcrumbs">
      <app-button (click)="modal.set({ period: null })"><app-icon name="plus" [size]="16" />Add Period</app-button>
    </app-page-header>

    <app-card [noPadding]="true">
      <app-table
        [columns]="columns"
        [rows]="rows()"
        [loading]="loading()"
        [hasActions]="true"
        [emptyMessage]="loadFailed() ? 'Could not load periods.' : 'No periods yet. Add period 1 to start building timetables.'"
      >
        <ng-template #rowActions let-row>
          <app-button variant="secondary" size="sm" aria-label="Edit" title="Edit" (click)="openEdit(row)"><app-icon name="pencil" [size]="14" /></app-button>
          @if (row.isActive) {
            <app-button variant="danger-ghost" size="sm" [disabled]="busyId() === row.periodId" (click)="toggle(row)"><app-icon name="toggle-left" [size]="14" />Deactivate</app-button>
          } @else {
            <app-button variant="secondary" size="sm" [disabled]="busyId() === row.periodId" (click)="toggle(row)"><app-icon name="toggle-right" [size]="14" />Activate</app-button>
          }
        </ng-template>
      </app-table>
    </app-card>

    @if (modal(); as m) {
      <app-period-form-modal [period]="m.period" [nextNumber]="nextNumber()" (saved)="onSaved($event)" (closed)="modal.set(null)" />
    }
  `,
})
export class PeriodListComponent implements OnInit {
  private readonly service = inject(TimetableService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'School Setup' }, { label: 'Periods' }];

  protected readonly periods = signal<Period[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busyId = signal<number | null>(null);
  /** null = closed; { period: null } = create; { period } = edit */
  protected readonly modal = signal<{ period: Period | null } | null>(null);

  protected readonly rows = computed<PeriodRow[]>(() =>
    [...this.periods()]
      .sort((a, b) => a.periodNumber - b.periodNumber)
      .map((p) => ({
        periodId: p.periodId,
        periodNumber: p.periodNumber,
        time: `${shortTime(p.startTime)} – ${shortTime(p.endTime)}`,
        usage: p.activeEntryCount === 0 ? '—' : `${p.activeEntryCount} ${p.activeEntryCount === 1 ? 'slot' : 'slots'}`,
        statusName: p.statusName,
        isActive: p.statusName === 'Active',
      })),
  );
  protected readonly nextNumber = computed(() => this.periods().reduce((max, p) => Math.max(max, p.periodNumber), 0) + 1);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .periods()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (list) => this.periods.set(list),
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  protected openEdit(row: PeriodRow): void {
    this.modal.set({ period: this.periods().find((p) => p.periodId === row.periodId) ?? null });
  }

  protected onSaved(saved: Period): void {
    this.modal.set(null);
    this.periods.update((list) => (list.some((p) => p.periodId === saved.periodId) ? list.map((p) => (p.periodId === saved.periodId ? saved : p)) : [...list, saved]));
  }

  protected async toggle(row: PeriodRow): Promise<void> {
    const deactivating = row.isActive;
    const confirmed = await this.confirmDialog.confirm({
      title: `${deactivating ? 'Deactivate' : 'Activate'} period?`,
      message: deactivating
        ? `Period ${row.periodNumber} will be hidden from every timetable. Nothing is deleted and you can activate it again later.`
        : `Period ${row.periodNumber} will appear in timetables again.`,
      confirmText: deactivating ? 'Deactivate' : 'Activate',
      variant: deactivating ? 'danger' : 'primary',
    });
    if (!confirmed) return;

    this.busyId.set(row.periodId);
    this.service
      .togglePeriod(row.periodId)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: (updated) => {
          this.periods.update((list) => list.map((p) => (p.periodId === updated.periodId ? updated : p)));
          this.toast.success(`Period ${row.periodNumber} is now ${updated.statusName}.`);
        },
        error: () => undefined, // already toasted (e.g. the period is still used by a timetable)
      });
  }
}
