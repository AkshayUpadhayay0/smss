import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { canAccess } from '../../../../core/config/route-roles';
import { AuthService } from '../../../../core/services/auth.service';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, ModalComponent, PageHeaderComponent } from '../../../../shared/components';
import { ScaleFormModalComponent } from '../../components/scale-form-modal/scale-form-modal.component';
import { GradeScale, GradeScaleListItem, ScalePreset } from '../../models/examinations.model';
import { ExaminationsService } from '../../services/examinations.service';

/** Grade scales: the school's own copies (percentage bands for marks, ratings for skills). School Admin edits; a Principal only reads. */
@Component({
  selector: 'app-grade-scales',
  imports: [ButtonComponent, CardComponent, IconComponent, ModalComponent, PageHeaderComponent, ScaleFormModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/exam-common.scss'],
  template: `
    <app-page-header title="Grade Scales" subtitle="How a percentage or a rating becomes a grade" icon="layers" [breadcrumbs]="breadcrumbs">
      @if (canEdit()) {
        <app-button variant="secondary" (click)="openPresets()"><app-icon name="download" [size]="16" />Start from a preset</app-button>
        <app-button (click)="editing.set({ scale: null })"><app-icon name="plus" [size]="16" />New scale</app-button>
      }
    </app-page-header>

    @if (loading()) {
      <app-card><p class="note" role="status">Loading grade scales…</p></app-card>
    } @else if (failed()) {
      <app-card><div class="note"><p role="alert">The grade scales could not be loaded.</p><app-button variant="secondary" size="sm" (click)="load()">Try again</app-button></div></app-card>
    } @else if (scales().length === 0) {
      <app-card><p class="note">No grade scales yet.{{ canEdit() ? ' Start from a preset (the CBSE Class IX-style scale, a simple A-E scale, A-B-C skill ratings) and adjust it.' : '' }}</p></app-card>
    } @else {
      <app-card [noPadding]="true">
        <div class="table-wrap">
          <table class="dense" aria-label="Grade scales">
            <thead><tr><th>Name</th><th>Kind</th><th class="num">Grades</th><th>Used by</th><th></th></tr></thead>
            <tbody>
              @for (s of scales(); track s.gradeScaleId) {
                <tr>
                  <td><strong>{{ s.scaleName }}</strong>@if (s.sourcePresetCode) { <span class="pill blue" title="Copied from a preset">preset copy</span> }</td>
                  <td>{{ s.kind === 'PERCENT_BANDS' ? 'Percentage bands' : 'Rating' }}</td>
                  <td class="num">{{ s.bandCount }}</td>
                  <td>{{ usedBy(s) }}</td>
                  <td>
                    <div class="row-actions">
                      <app-button variant="secondary" size="sm" [disabled]="busyId() === s.gradeScaleId" (click)="open(s)">{{ canEdit() ? 'Edit' : 'View' }}</app-button>
                      @if (canEdit()) { <app-button variant="danger-ghost" size="sm" aria-label="Remove" [disabled]="busyId() === s.gradeScaleId" (click)="remove(s)"><app-icon name="trash-2" [size]="14" /></app-button> }
                    </div>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </app-card>
    }

    @if (editing(); as e) {
      <app-scale-form-modal [scale]="e.scale" [readonly]="!canEdit()" (saved)="onSaved()" (closed)="editing.set(null)" />
    }

    @if (presets(); as list) {
      <app-modal title="Start from a preset" (closed)="presets.set(null)">
        <div class="stack">
          @for (p of list; track p.code) {
            <div class="preset">
              <div>
                <strong>{{ p.name }}</strong>
                <p class="note">{{ p.note }}</p>
                <p class="note"><small>{{ summary(p) }}</small></p>
              </div>
              <app-button size="sm" [disabled]="busyCode() === p.code" (click)="copy(p)">{{ busyCode() === p.code ? 'Copying…' : 'Copy to my school' }}</app-button>
            </div>
          }
        </div>
        <ng-container modal-footer><app-button variant="secondary" (click)="presets.set(null)">Close</app-button></ng-container>
      </app-modal>
    }
  `,
  styles: `
    .preset {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: var(--space-4);
      padding-bottom: var(--space-4);
      border-bottom: 1px solid var(--color-border);
    }
    .muted { color: var(--color-text-muted); }
  `,
})
export class GradeScalesComponent implements OnInit {
  private readonly service = inject(ExaminationsService);
  private readonly auth = inject(AuthService);
  private readonly confirm = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'Examinations' }, { label: 'Grade Scales' }];
  /** Whether this role may change things, read from the same ROUTE_ROLES as the guard (the API enforces it as well). */
  protected readonly canEdit = computed(() => canAccess('/examinations/schemes/new', this.auth.currentUserRoles()));

  protected readonly scales = signal<GradeScaleListItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly busyId = signal<number | null>(null);
  protected readonly busyCode = signal<string | null>(null);
  protected readonly editing = signal<{ scale: GradeScale | null } | null>(null);
  protected readonly presets = signal<ScalePreset[] | null>(null);

  ngOnInit(): void { this.load(); }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.service.scales().pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: (l) => this.scales.set(l), error: () => this.failed.set(true) });
  }

  protected usedBy(s: GradeScaleListItem): string {
    const parts = [s.schemeCount ? `${s.schemeCount} scheme${s.schemeCount === 1 ? '' : 's'}` : '', s.areaCount ? `${s.areaCount} activity area${s.areaCount === 1 ? '' : 's'}` : ''].filter(Boolean);
    return parts.length ? parts.join(', ') : 'Not used yet';
  }

  protected summary(p: ScalePreset): string {
    return p.bands.map((b) => (b.minPercent == null ? b.gradeCode : `${b.gradeCode} ${b.minPercent}%+`)).join(' · ');
  }

  protected open(s: GradeScaleListItem): void {
    this.busyId.set(s.gradeScaleId);
    this.service.scale(s.gradeScaleId).pipe(finalize(() => this.busyId.set(null)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: (full) => this.editing.set({ scale: full }), error: () => undefined });
  }

  protected openPresets(): void {
    this.service.scalePresets().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (l) => this.presets.set(l), error: () => undefined });
  }

  protected copy(p: ScalePreset): void {
    this.busyCode.set(p.code);
    this.service.scaleFromPreset(p.code).pipe(finalize(() => this.busyCode.set(null)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.toast.success('Copied: edit it to suit your school.'); this.presets.set(null); this.load(); },
      error: () => undefined, // e.g. a scale with that name already exists
    });
  }

  protected onSaved(): void { this.editing.set(null); this.load(); }

  protected async remove(s: GradeScaleListItem): Promise<void> {
    if (!(await this.confirm.confirm({ title: 'Remove this grade scale?', message: `"${s.scaleName}" will no longer be offered. Schemes and areas that use it must be changed first.`, confirmText: 'Remove', variant: 'danger' }))) return;
    this.busyId.set(s.gradeScaleId);
    this.service.deleteScale(s.gradeScaleId).pipe(finalize(() => this.busyId.set(null))).subscribe({ next: () => { this.toast.success('Grade scale removed.'); this.load(); }, error: () => undefined });
  }
}
