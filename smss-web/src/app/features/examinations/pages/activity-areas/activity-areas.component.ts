import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, forkJoin } from 'rxjs';
import { canAccess } from '../../../../core/config/route-roles';
import { AuthService } from '../../../../core/services/auth.service';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, ModalComponent, PageHeaderComponent } from '../../../../shared/components';
import { ActivityArea, AreaCategory, CATEGORY_LABEL, GradeScaleListItem } from '../../models/examinations.model';
import { ExaminationsService } from '../../services/examinations.service';

/** Co-scholastic and discipline areas (Art & Craft, Discipline, Cleanliness ...) graded on a rating scale. School-wide, shared by every scheme. */
@Component({
  selector: 'app-activity-areas',
  imports: [ButtonComponent, CardComponent, IconComponent, ModalComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/exam-common.scss'],
  template: `
    <app-page-header title="Activity Areas" subtitle="Co-scholastic and discipline areas graded on the report card" icon="circle-check" [breadcrumbs]="breadcrumbs">
      @if (canEdit()) {
        <app-button variant="secondary" [disabled]="starting()" (click)="starter()"><app-icon name="plus" [size]="16" />{{ starting() ? 'Adding…' : 'Add the usual areas' }}</app-button>
        <app-button [disabled]="ratingScales().length === 0" (click)="startEdit(null)"><app-icon name="plus" [size]="16" />New area</app-button>
      }
    </app-page-header>

    @if (loading()) {
      <app-card><p class="note" role="status">Loading activity areas…</p></app-card>
    } @else if (failed()) {
      <app-card><div class="note"><p role="alert">The activity areas could not be loaded.</p><app-button variant="secondary" size="sm" (click)="load()">Try again</app-button></div></app-card>
    } @else if (areas().length === 0) {
      <app-card><p class="note">No activity areas yet.{{ canEdit() ? ' "Add the usual areas" creates Discipline, Cleanliness, Punctuality, Art & Craft, Music, Physical Education and Computer Skills on an A-B-C scale.' : '' }}</p></app-card>
    } @else {
      @for (g of grouped(); track g.category) {
        <app-card [title]="label[g.category]" [noPadding]="true">
          <div class="table-wrap">
            <table class="dense" [attr.aria-label]="label[g.category]">
              <thead><tr><th>Area</th><th>Graded on</th><th class="num">Order</th><th></th></tr></thead>
              <tbody>
                @for (a of g.items; track a.areaId) {
                  <tr>
                    <td><strong>{{ a.name }}</strong></td><td>{{ a.ratingScaleName }}</td><td class="num">{{ a.sequence }}</td>
                    <td>
                      @if (canEdit()) {
                        <div class="row-actions">
                          <app-button variant="secondary" size="sm" (click)="startEdit(a)">Edit</app-button>
                          <app-button variant="danger-ghost" size="sm" aria-label="Remove" (click)="remove(a)"><app-icon name="trash-2" [size]="14" /></app-button>
                        </div>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </app-card>
      }
    }

    @if (form(); as f) {
      <app-modal [title]="f.areaId ? 'Edit activity area' : 'New activity area'" [dismissible]="!saving()" (closed)="form.set(null)">
        <div class="stack">
          <label class="field"><span>Name</span><input type="text" maxlength="60" [value]="f.name" (input)="patch('name', $any($event.target).value)" aria-label="Area name" /></label>
          <div class="grid-2">
            <label class="field"><span>Category</span>
              <select (change)="patch('category', $any($event.target).value)" aria-label="Category">
                @for (c of categories; track c) { <option [value]="c" [selected]="c === f.category">{{ label[c] }}</option> }
              </select>
            </label>
            <label class="field"><span>Graded on</span>
              <select (change)="patch('ratingScaleId', $any($event.target).value)" aria-label="Rating scale">
                @for (s of ratingScales(); track s.gradeScaleId) { <option [value]="s.gradeScaleId" [selected]="String(s.gradeScaleId) === String(f.ratingScaleId)">{{ s.scaleName }}</option> }
              </select>
            </label>
            <label class="field"><span>Order</span><input type="number" min="0" max="1000" [value]="f.sequence" (input)="patch('sequence', $any($event.target).value)" aria-label="Order" /></label>
          </div>
        </div>
        <ng-container modal-footer>
          <app-button variant="secondary" [disabled]="saving()" (click)="form.set(null)">Cancel</app-button>
          <app-button [disabled]="saving() || !f.name.trim() || !f.ratingScaleId" (click)="save()">{{ saving() ? 'Saving…' : 'Save' }}</app-button>
        </ng-container>
      </app-modal>
    }
  `,
})
export class ActivityAreasComponent implements OnInit {
  private readonly service = inject(ExaminationsService);
  private readonly auth = inject(AuthService);
  private readonly confirm = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly String = String;
  protected readonly breadcrumbs = [{ label: 'Examinations' }, { label: 'Activity Areas' }];
  protected readonly label = CATEGORY_LABEL;
  protected readonly categories: AreaCategory[] = ['CO_SCHOLASTIC', 'DISCIPLINE', 'OTHER'];
  protected readonly canEdit = computed(() => canAccess('/examinations/schemes/new', this.auth.currentUserRoles()));

  protected readonly areas = signal<ActivityArea[]>([]);
  protected readonly ratingScales = signal<GradeScaleListItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly starting = signal(false);
  protected readonly saving = signal(false);
  protected readonly form = signal<{ areaId: number | null; name: string; category: AreaCategory; ratingScaleId: number | string; sequence: number | string } | null>(null);
  protected readonly grouped = computed(() => this.categories.map((c) => ({ category: c, items: this.areas().filter((a) => a.category === c) })).filter((g) => g.items.length > 0));

  ngOnInit(): void { this.load(); }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    forkJoin({ areas: this.service.areas(), scales: this.service.scales() }).pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (r) => { this.areas.set(r.areas); this.ratingScales.set(r.scales.filter((s) => s.kind === 'RATING')); },
      error: () => this.failed.set(true),
    });
  }

  protected starter(): void {
    this.starting.set(true);
    this.service.starterAreas().pipe(finalize(() => this.starting.set(false))).subscribe({
      next: (r) => { this.toast.success(r.created > 0 ? `${r.created} area(s) added.` : 'The usual areas are already there.'); this.load(); },
      error: () => undefined,
    });
  }

  protected startEdit(a: ActivityArea | null): void {
    this.form.set({ areaId: a?.areaId ?? null, name: a?.name ?? '', category: a?.category ?? 'CO_SCHOLASTIC', ratingScaleId: a?.ratingScaleId ?? this.ratingScales()[0]?.gradeScaleId ?? '', sequence: a?.sequence ?? this.areas().length + 1 });
  }

  protected patch(field: 'name' | 'category' | 'ratingScaleId' | 'sequence', value: string): void { this.form.update((f) => (f ? { ...f, [field]: value } : f)); }

  protected save(): void {
    const f = this.form();
    if (!f || this.saving()) return;
    const body = { category: f.category, name: f.name.trim(), sequence: Number(f.sequence) || 0, ratingScaleId: Number(f.ratingScaleId) };
    this.saving.set(true);
    (f.areaId ? this.service.updateArea(f.areaId, body) : this.service.createArea(body)).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: () => { this.toast.success('Activity area saved.'); this.form.set(null); this.load(); },
      error: () => undefined,
    });
  }

  protected async remove(a: ActivityArea): Promise<void> {
    if (!(await this.confirm.confirm({ title: 'Remove this area?', message: `"${a.name}" will stop being offered. Grades already given stay on published cards.`, confirmText: 'Remove', variant: 'danger' }))) return;
    this.service.deleteArea(a.areaId).subscribe({ next: () => { this.toast.success('Activity area removed.'); this.load(); }, error: () => undefined });
  }
}
