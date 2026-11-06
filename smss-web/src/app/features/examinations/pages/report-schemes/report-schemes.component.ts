import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { canAccess } from '../../../../core/config/route-roles';
import { AuthService } from '../../../../core/services/auth.service';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent } from '../../../../shared/components';
import { MODE_LABEL, SchemeListItem } from '../../models/examinations.model';
import { ExaminationsService } from '../../services/examinations.service';

/** Report schemes: one per class group per academic year (terms, exams, subjects, pass rules, card layout). */
@Component({
  selector: 'app-report-schemes',
  imports: [ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/exam-common.scss'],
  template: `
    <app-page-header title="Report Schemes" subtitle="Terms, exams, subjects and pass rules for a group of classes" icon="file-text" [breadcrumbs]="breadcrumbs">
      @if (canEdit()) {
        <app-button variant="secondary" (click)="blank()"><app-icon name="plus" [size]="16" />Blank scheme</app-button>
        <app-button (click)="router.navigate(['/examinations/schemes/new'])"><app-icon name="plus" [size]="16" />New from a preset</app-button>
      }
    </app-page-header>

    @if (loading()) {
      <app-card><p class="note" role="status">Loading report schemes…</p></app-card>
    } @else if (failed()) {
      <app-card><div class="note"><p role="alert">The report schemes could not be loaded.</p><app-button variant="secondary" size="sm" (click)="load()">Try again</app-button></div></app-card>
    } @else if (schemes().length === 0) {
      <app-card>
        <p class="note">
          No report schemes yet.
          @if (canEdit()) { Pick one of three ready-made shapes (term-wise out of 100, two papers, or a pre-primary skill grid), choose the classes and you are done. <a routerLink="/examinations/schemes/new">Start from a preset</a> }
        </p>
      </app-card>
    } @else {
      @if (years().length > 1) {
        <div class="bar">
          <label class="field"><span>Academic year</span>
            <select [value]="year()" (change)="year.set($any($event.target).value)" aria-label="Academic year">
              @for (y of years(); track y) { <option [value]="y" [selected]="y === year()">{{ y }}</option> }
            </select>
          </label>
        </div>
      }
      <app-card [noPadding]="true">
        <div class="table-wrap">
          <table class="dense" aria-label="Report schemes">
            <thead><tr><th>Scheme</th><th>Classes</th><th>Mode</th><th class="num">Terms</th><th class="num">Exams</th><th class="num">Subjects</th><th></th></tr></thead>
            <tbody>
              @for (s of shown(); track s.schemeId) {
                <tr>
                  <td><a [routerLink]="['/examinations/schemes', s.schemeId]"><strong>{{ s.schemeName }}</strong></a><br /><small class="note">{{ s.academicYearName }}</small></td>
                  <td>{{ s.classNames.length ? s.classNames.join(', ') : 'No classes yet' }}</td>
                  <td>{{ mode[s.evaluationMode] }}</td>
                  <td class="num">{{ s.termCount }}</td>
                  <td class="num">{{ s.examCount }}</td>
                  <td class="num">{{ s.subjectCount }}</td>
                  <td>
                    <div class="row-actions">
                      <app-button variant="secondary" size="sm" (click)="router.navigate(['/examinations/schemes', s.schemeId])">{{ canEdit() ? 'Open' : 'View' }}</app-button>
                      @if (canEdit()) {
                        <app-button variant="secondary" size="sm" aria-label="Copy" title="Copy this scheme" [disabled]="busyId() === s.schemeId" (click)="clone(s)"><app-icon name="layers" [size]="14" /></app-button>
                        <app-button variant="danger-ghost" size="sm" aria-label="Remove" [disabled]="busyId() === s.schemeId" (click)="remove(s)"><app-icon name="trash-2" [size]="14" /></app-button>
                      }
                    </div>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </app-card>
    }
  `,
})
export class ReportSchemesComponent implements OnInit {
  protected readonly router = inject(Router);
  private readonly service = inject(ExaminationsService);
  private readonly auth = inject(AuthService);
  private readonly confirm = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'Examinations' }, { label: 'Report Schemes' }];
  protected readonly mode = MODE_LABEL;
  protected readonly canEdit = computed(() => canAccess('/examinations/schemes/new', this.auth.currentUserRoles()));

  protected readonly schemes = signal<SchemeListItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly busyId = signal<number | null>(null);
  protected readonly year = signal('');
  protected readonly years = computed(() => [...new Set(this.schemes().map((s) => s.academicYearName))]);
  protected readonly shown = computed(() => this.schemes().filter((s) => !this.year() || s.academicYearName === this.year()));

  ngOnInit(): void { this.load(); }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.service.schemes().pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (l) => { this.schemes.set(l); if (!this.year() || !l.some((s) => s.academicYearName === this.year())) this.year.set(l[0]?.academicYearName ?? ''); },
      error: () => this.failed.set(true),
    });
  }

  protected blank(): void { void this.router.navigate(['/examinations/schemes/blank']); }

  protected async clone(s: SchemeListItem): Promise<void> {
    if (!(await this.confirm.confirm({ title: 'Copy this scheme?', message: `A copy of "${s.schemeName}" is made in the same year, without classes (a class can be in one scheme per year). Open it to choose its classes.`, confirmText: 'Copy' }))) return;
    this.busyId.set(s.schemeId);
    this.service.cloneScheme(s.schemeId).pipe(finalize(() => this.busyId.set(null))).subscribe({
      next: (c) => { this.toast.success('Scheme copied.'); void this.router.navigate(['/examinations/schemes', c.schemeId]); },
      error: () => undefined,
    });
  }

  protected async remove(s: SchemeListItem): Promise<void> {
    if (!(await this.confirm.confirm({ title: 'Remove this scheme?', message: `"${s.schemeName}" and its terms, exams and subject set-up will be removed, and its classes freed for another scheme.`, confirmText: 'Remove', variant: 'danger' }))) return;
    this.busyId.set(s.schemeId);
    this.service.deleteScheme(s.schemeId).pipe(finalize(() => this.busyId.set(null))).subscribe({ next: () => { this.toast.success('Scheme removed.'); this.load(); }, error: () => undefined });
  }
}
