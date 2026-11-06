import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, IconComponent, ModalComponent } from '../../../../shared/components';
import { BandRequest, GradeScale, ScaleKind } from '../../models/examinations.model';
import { ExaminationsService } from '../../services/examinations.service';

interface Row { code: string; label: string; min: string; fail: boolean }

/**
 * Create / edit / view one grade scale. A percentage scale stores only each grade's LOWER edge (inclusive); the upper edge shown is simply the next grade's
 * lower edge, so no percentage, however many decimals, can fall between two grades. A rating scale (A, B, C ...) has no percentages.
 */
@Component({
  selector: 'app-scale-form-modal',
  imports: [ModalComponent, ButtonComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/exam-common.scss'],
  template: `
    <app-modal [title]="title()" [dismissible]="!saving()" (closed)="closed.emit()">
      <div class="stack">
        <div class="grid-2">
          <label class="field"><span>Name</span><input type="text" maxlength="80" [value]="name()" [disabled]="readonly()" (input)="name.set($any($event.target).value)" aria-label="Scale name" /></label>
          <label class="field">
            <span>Kind</span>
            <select [disabled]="readonly() || !!scale()" (change)="setKind($any($event.target).value)" aria-label="Scale kind">
              <option value="PERCENT_BANDS" [selected]="kind() === 'PERCENT_BANDS'">Percentage bands (marks schemes)</option>
              <option value="RATING" [selected]="kind() === 'RATING'">Rating A, B, C (skills and activities)</option>
            </select>
          </label>
        </div>

        @if (kind() === 'PERCENT_BANDS') {
          <p class="note">Enter where each grade <strong>starts</strong> (inclusive). It runs up to just under the next grade's start. The lowest grade must start at 0.</p>
        } @else {
          <p class="note">The grades in the order they should appear, best first.</p>
        }

        <div class="table-wrap">
          <table class="dense">
            <thead>
              <tr>
                <th>Code</th><th>Label</th>
                @if (kind() === 'PERCENT_BANDS') { <th>From %</th><th>Up to</th> }
                <th>Counts as fail</th><th></th>
              </tr>
            </thead>
            <tbody>
              @for (r of view(); track $index) {
                <tr>
                  <td><input type="text" maxlength="10" [value]="r.row.code" [disabled]="readonly()" (input)="edit(r.index, 'code', $any($event.target).value)" [attr.aria-label]="'Grade code ' + ($index + 1)" /></td>
                  <td><input type="text" maxlength="60" [value]="r.row.label" [disabled]="readonly()" (input)="edit(r.index, 'label', $any($event.target).value)" [attr.aria-label]="'Grade label ' + ($index + 1)" /></td>
                  @if (kind() === 'PERCENT_BANDS') {
                    <td><input type="number" min="0" max="100" step="0.01" [value]="r.row.min" [disabled]="readonly()" (input)="edit(r.index, 'min', $any($event.target).value)" [attr.aria-label]="'Starts at percent ' + ($index + 1)" /></td>
                    <td class="muted">{{ r.upTo }}</td>
                  }
                  <td><input type="checkbox" [checked]="r.row.fail" [disabled]="readonly()" (change)="edit(r.index, 'fail', $any($event.target).checked)" [attr.aria-label]="'Fail grade ' + ($index + 1)" /></td>
                  <td>
                    @if (!readonly()) { <app-button variant="danger-ghost" size="sm" aria-label="Remove grade" (click)="remove(r.index)"><app-icon name="trash-2" [size]="14" /></app-button> }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        @if (!readonly()) {
          <div><app-button variant="secondary" size="sm" (click)="add()"><app-icon name="plus" [size]="14" />Add a grade</app-button></div>
        }
        @if (problem(); as p) { <p class="error-text" role="alert">{{ p }}</p> }
      </div>

      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">{{ readonly() ? 'Close' : 'Cancel' }}</app-button>
        @if (!readonly()) { <app-button [disabled]="saving() || !!problem()" (click)="save()">{{ saving() ? 'Saving…' : 'Save scale' }}</app-button> }
      </ng-container>
    </app-modal>
  `,
})
export class ScaleFormModalComponent implements OnInit {
  private readonly service = inject(ExaminationsService);
  private readonly toast = inject(ToastService);

  /** The scale to edit or view; null creates a new one. */
  readonly scale = input<GradeScale | null>(null);
  readonly readonly = input(false);
  readonly saved = output<GradeScale>();
  readonly closed = output<void>();

  protected readonly name = signal('');
  protected readonly kind = signal<ScaleKind>('PERCENT_BANDS');
  protected readonly rows = signal<Row[]>([]);
  protected readonly saving = signal(false);

  protected readonly title = computed(() => (this.readonly() ? 'Grade scale' : this.scale() ? 'Edit grade scale' : 'New grade scale'));

  /** Percent scales listed lowest first for editing (the server stores them that way) with the derived upper edge. */
  protected readonly view = computed(() => {
    const pct = this.kind() === 'PERCENT_BANDS';
    const indexed = this.rows().map((row, index) => ({ row, index }));
    if (pct) indexed.sort((a, b) => (parseFloat(a.row.min) || 0) - (parseFloat(b.row.min) || 0));
    return indexed.map((x, i) => ({ ...x, upTo: pct ? (i + 1 < indexed.length ? `${this.num(indexed[i + 1].row.min)}` : '100') : '' }));
  });

  protected readonly problem = computed<string | null>(() => {
    const rows = this.rows();
    if (this.readonly()) return null;
    if (!this.name().trim()) return 'Give the scale a name.';
    if (rows.length === 0) return 'Add at least one grade.';
    if (rows.some((r) => !r.code.trim() || !r.label.trim())) return 'Every grade needs a code and a label.';
    if (new Set(rows.map((r) => r.code.trim().toUpperCase())).size !== rows.length) return 'Grade codes must be different from each other.';
    if (this.kind() === 'PERCENT_BANDS') {
      const mins = rows.map((r) => (r.min.trim() === '' ? NaN : Number(r.min)));
      if (mins.some((m) => Number.isNaN(m) || m < 0 || m > 100)) return 'Every grade needs a starting percentage between 0 and 100.';
      if (!mins.includes(0)) return 'The lowest grade must start at 0%.';
      if (new Set(mins).size !== mins.length) return 'Two grades start at the same percentage.';
    }
    return null;
  });

  ngOnInit(): void {
    const s = this.scale();
    if (s) {
      this.name.set(s.scaleName);
      this.kind.set(s.kind);
      this.rows.set([...s.bands].reverse().map((b) => ({ code: b.gradeCode, label: b.label, min: b.minPercent == null ? '' : String(b.minPercent), fail: b.isFail })));
      if (s.kind === 'RATING') this.rows.set(s.bands.map((b) => ({ code: b.gradeCode, label: b.label, min: '', fail: b.isFail })));
    } else {
      this.rows.set([{ code: 'A', label: '', min: '0', fail: false }]);
    }
  }

  private num(v: string): string { return String(parseFloat(v)); }

  protected setKind(k: string): void {
    this.kind.set(k as ScaleKind);
    this.rows.set(this.rows().map((r) => ({ ...r, min: k === 'RATING' ? '' : r.min })));
  }

  protected edit(index: number, field: 'code' | 'label' | 'min' | 'fail', value: string | boolean): void {
    this.rows.update((rs) => rs.map((r, i) => (i === index ? { ...r, [field]: value } : r)));
  }

  protected add(): void { this.rows.update((rs) => [...rs, { code: '', label: '', min: '', fail: false }]); }
  protected remove(index: number): void { this.rows.update((rs) => rs.filter((_, i) => i !== index)); }

  protected save(): void {
    if (this.saving() || this.problem()) return;
    const pct = this.kind() === 'PERCENT_BANDS';
    const bands: BandRequest[] = this.rows().map((r) => ({ gradeCode: r.code.trim(), label: r.label.trim(), minPercent: pct ? Number(r.min) : null, isFail: r.fail }));
    const body = { name: this.name().trim(), kind: this.kind(), bands };
    this.saving.set(true);
    const s = this.scale();
    (s ? this.service.updateScale(s.gradeScaleId, body) : this.service.createScale(body)).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (saved) => { this.toast.success('Grade scale saved.'); this.saved.emit(saved); },
      error: () => undefined, // already toasted by the interceptor (e.g. the name is taken)
    });
  }
}
