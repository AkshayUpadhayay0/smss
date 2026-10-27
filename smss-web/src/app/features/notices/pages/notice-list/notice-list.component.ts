import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, SelectComponent, SelectOption, TableColumn, TableComponent } from '../../../../shared/components';
import { NoticeFormModalComponent } from '../../components/notice-form-modal/notice-form-modal.component';
import { Notice, NoticeAudience } from '../../models/notice.model';
import { NoticeService } from '../../services/notice.service';
import { AUDIENCE_LABELS, audienceLabel, publishedLabel, snippet } from '../../utils/notice-format';

interface NoticeRow {
  noticeId: number;
  title: string;
  preview: string;
  audience: string;
  createdByName: string;
  published: string;
}

const COLUMNS: TableColumn<NoticeRow>[] = [
  { key: 'title', label: 'Title' },
  { key: 'preview', label: 'Notice' },
  { key: 'audience', label: 'Sent to' },
  { key: 'createdByName', label: 'Published by' },
  { key: 'published', label: 'Published' },
];

/** School Admin / Principal: every notice of the school, filterable by audience and published date; publish, edit and delete. */
@Component({
  selector: 'app-notice-list',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, SelectComponent, TableComponent, NoticeFormModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './notice-list.component.scss',
  template: `
    <app-page-header title="Notices" subtitle="Announcements to staff, students and guardians" icon="bell" [breadcrumbs]="breadcrumbs">
      <app-button (click)="modal.set({ notice: null })"><app-icon name="plus" [size]="16" />New Notice</app-button>
    </app-page-header>

    <app-card>
      <div class="filters">
        <app-select label="Sent to" [options]="audienceOptions" [formControl]="audienceControl" />
        <app-input label="Published from" type="date" [formControl]="fromControl" />
        <app-input label="Published to" type="date" [formControl]="toControl" />
        <p class="hint">{{ rows().length }} {{ rows().length === 1 ? 'notice' : 'notices' }}</p>
      </div>
    </app-card>

    <app-card [noPadding]="true">
      <app-table [columns]="columns" [rows]="rows()" [loading]="loading()" [hasActions]="true" [emptyMessage]="loadFailed() ? 'Could not load notices.' : 'No notices match.'">
        <ng-template #rowActions let-row>
          <app-button variant="secondary" size="sm" aria-label="Edit" title="Edit" (click)="openEdit(row)"><app-icon name="pencil" [size]="14" /></app-button>
          <app-button variant="danger-ghost" size="sm" aria-label="Delete" title="Delete" [disabled]="busyId() === row.noticeId" (click)="remove(row)"><app-icon name="trash-2" [size]="14" /></app-button>
        </ng-template>
      </app-table>
    </app-card>

    @if (modal(); as m) {
      <app-notice-form-modal [notice]="m.notice" (saved)="onSaved()" (closed)="modal.set(null)" />
    }
  `,
})
export class NoticeListComponent implements OnInit {
  private readonly service = inject(NoticeService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'Notices' }];
  protected readonly audienceOptions: SelectOption[] = [
    { label: 'All audiences', value: '' },
    ...(Object.keys(AUDIENCE_LABELS) as NoticeAudience[]).map((a) => ({ label: AUDIENCE_LABELS[a], value: a })),
  ];
  protected readonly audienceControl = new FormControl('', { nonNullable: true });
  protected readonly fromControl = new FormControl('', { nonNullable: true });
  protected readonly toControl = new FormControl('', { nonNullable: true });

  protected readonly items = signal<Notice[]>([]);
  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly busyId = signal<number | null>(null);
  /** null = closed; { notice: null } = publish; { notice } = edit */
  protected readonly modal = signal<{ notice: Notice | null } | null>(null);

  protected readonly rows = computed<NoticeRow[]>(() =>
    this.items().map((n) => ({
      noticeId: n.noticeId, title: n.title, preview: snippet(n.body, 80), audience: audienceLabel(n.audience),
      createdByName: n.createdByName ?? '—', published: publishedLabel(n.publishedAt),
    })),
  );

  ngOnInit(): void {
    for (const c of [this.audienceControl, this.fromControl, this.toControl]) c.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .list({ audience: this.audienceControl.value as NoticeAudience | '', from: this.fromControl.value, to: this.toControl.value })
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (list) => this.items.set(list), error: () => this.loadFailed.set(true) });
  }

  protected openEdit(row: NoticeRow): void {
    this.modal.set({ notice: this.items().find((n) => n.noticeId === row.noticeId) ?? null });
  }

  protected onSaved(): void {
    this.modal.set(null);
    this.load();
    this.service.refreshUnread();
  }

  protected async remove(row: NoticeRow): Promise<void> {
    const confirmed = await this.confirmDialog.confirm({
      title: 'Delete notice?',
      message: `"${row.title}" will be hidden from everyone it was sent to.`,
      confirmText: 'Delete',
      variant: 'danger',
    });
    if (!confirmed) return;
    this.busyId.set(row.noticeId);
    this.service
      .remove(row.noticeId)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: () => {
          this.items.update((l) => l.filter((n) => n.noticeId !== row.noticeId));
          this.toast.success('Notice deleted.');
          this.service.refreshUnread();
        },
        error: () => this.load(), // a different Principal's notice, or already removed: show the truth
      });
  }
}
