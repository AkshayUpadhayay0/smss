import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize, forkJoin, of } from 'rxjs';
import { canAccess } from '../../../../core/config/route-roles';
import { AuthService } from '../../../../core/services/auth.service';
import { ButtonComponent, CardComponent, PageHeaderComponent } from '../../../../shared/components';
import { AcademicYear } from '../../../school-setup/models/academic-year.model';
import { AcademicYearService } from '../../../school-setup/services/academic-year.service';
import { SchemeBasicsComponent } from '../../components/scheme-basics/scheme-basics.component';
import { SchemeClassesComponent } from '../../components/scheme-classes/scheme-classes.component';
import { SchemeMatrixComponent } from '../../components/scheme-matrix/scheme-matrix.component';
import { SchemeSkillsComponent } from '../../components/scheme-skills/scheme-skills.component';
import { SchemeSubjectsComponent } from '../../components/scheme-subjects/scheme-subjects.component';
import { SchemeTermsComponent } from '../../components/scheme-terms/scheme-terms.component';
import { GradeScaleListItem, MODE_LABEL, ReportTemplate, Scheme } from '../../models/examinations.model';
import { ExaminationsService } from '../../services/examinations.service';

type Tab = 'basics' | 'classes' | 'terms' | 'subjects' | 'matrix' | 'skills';

/** One report scheme, part by part. The tabs follow the order a scheme is built in: details, classes, terms and exams, subjects, subject matrix, skills. */
@Component({
  selector: 'app-scheme-editor',
  imports: [ButtonComponent, CardComponent, PageHeaderComponent, RouterLink, SchemeBasicsComponent, SchemeClassesComponent, SchemeTermsComponent, SchemeSubjectsComponent, SchemeMatrixComponent, SchemeSkillsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/exam-common.scss'],
  template: `
    <app-page-header [title]="scheme()?.schemeName ?? 'New blank scheme'" [subtitle]="subtitle()" icon="file-text" [breadcrumbs]="breadcrumbs()">
      <a routerLink="/examinations/schemes" class="back">Back to schemes</a>
    </app-page-header>

    @if (loading()) {
      <app-card><p class="note" role="status">Loading the scheme…</p></app-card>
    } @else if (failed()) {
      <app-card><div class="note"><p role="alert">The scheme could not be loaded.</p><app-button variant="secondary" size="sm" (click)="load()">Try again</app-button></div></app-card>
    } @else {
      @if (scheme(); as s) {
        @if (s.warnings.length > 0) {
          <ul class="warnings" aria-label="Things to look at">@for (w of s.warnings; track $index) { <li>{{ w }}</li> }</ul>
        }
        <div class="tabs" role="tablist" aria-label="Scheme sections">
          @for (t of tabs(); track t.key) {
            <button type="button" role="tab" [attr.aria-selected]="tab() === t.key" [class.active]="tab() === t.key" (click)="tab.set(t.key)">{{ t.label }}</button>
          }
        </div>
        <div role="tabpanel">
          @switch (tab()) {
            @case ('basics') { <app-scheme-basics [scheme]="s" [canEdit]="canEdit()" [years]="years()" [scales]="scales()" [templates]="templates()" (saved)="update($event)" /> }
            @case ('classes') { <app-scheme-classes [scheme]="s" [canEdit]="canEdit()" (saved)="update($event)" /> }
            @case ('terms') { <app-scheme-terms [scheme]="s" [canEdit]="canEdit()" (saved)="update($event)" /> }
            @case ('subjects') { <app-scheme-subjects [scheme]="s" [canEdit]="canEdit()" (saved)="update($event)" /> }
            @case ('matrix') { <app-scheme-matrix [scheme]="s" [canEdit]="canEdit()" (saved)="update($event)" /> }
            @case ('skills') { <app-scheme-skills [scheme]="s" [canEdit]="canEdit()" (saved)="update($event)" /> }
          }
        </div>
      } @else {
        <app-scheme-basics [scheme]="null" [canEdit]="canEdit()" [years]="years()" [scales]="scales()" [templates]="templates()" (saved)="created($event)" />
      }
    }
  `,
  styles: `
    .back { color: var(--color-text-muted); }
    .tabs { display: flex; gap: var(--space-2); margin-bottom: var(--space-4); border-bottom: 1px solid var(--color-border); overflow-x: auto; }
    .tabs button { padding: var(--space-3) var(--space-5); border: 0; border-bottom: 2px solid transparent; background: none; color: var(--color-text-muted); font: inherit; font-weight: var(--weight-medium); cursor: pointer; white-space: nowrap; }
    .tabs button:hover { color: var(--color-text); }
    .tabs button.active { color: var(--color-primary); border-bottom-color: var(--color-primary); }
    .tabs button:focus-visible { outline: 2px solid var(--color-primary); outline-offset: -2px; }
  `,
})
export class SchemeEditorComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly service = inject(ExaminationsService);
  private readonly yearsApi = inject(AcademicYearService);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly scheme = signal<Scheme | null>(null);
  protected readonly templates = signal<ReportTemplate[]>([]);
  protected readonly scales = signal<GradeScaleListItem[]>([]);
  protected readonly years = signal<AcademicYear[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly tab = signal<Tab>('basics');
  private readonly id = signal<number | null>(null);

  /** The editing role (School Admin) is the one allowed on the "new" route; a Principal gets the same screens read-only. */
  protected readonly canEdit = computed(() => canAccess('/examinations/schemes/new', this.auth.currentUserRoles()));
  protected readonly subtitle = computed(() => {
    const s = this.scheme();
    return s ? `${s.academicYearName} · ${MODE_LABEL[s.evaluationMode]}${s.classNames.length ? ' · ' + s.classNames.join(', ') : ''}` : 'A scheme built part by part';
  });
  protected readonly breadcrumbs = computed(() => [{ label: 'Examinations' }, { label: 'Report Schemes', link: '/examinations/schemes' }, { label: this.scheme()?.schemeName ?? 'New' }]);
  protected readonly tabs = computed<{ key: Tab; label: string }[]>(() => {
    const s = this.scheme();
    const all: { key: Tab; label: string }[] = [
      { key: 'basics', label: 'Details' }, { key: 'classes', label: 'Classes' }, { key: 'terms', label: 'Terms & exams' }, { key: 'subjects', label: 'Subjects' }, { key: 'matrix', label: 'Subject matrix' },
    ];
    // skills only matter for graded cells (a grades-only scheme, or a marks scheme that has a GRADE cell)
    if (s && (s.evaluationMode === 'GRADES' || s.cells.some((c) => c.entryMode === 'GRADE'))) all.push({ key: 'skills', label: 'Skills' });
    return all;
  });

  ngOnInit(): void { this.load(); }

  protected load(): void {
    const raw = this.route.snapshot.paramMap.get('schemeId');
    const id = raw && /^\d+$/.test(raw) ? Number(raw) : null;
    this.id.set(id);
    this.loading.set(true);
    this.failed.set(false);
    forkJoin({
      scheme: id == null ? of(null) : this.service.scheme(id),
      templates: this.service.templates(),
      scales: this.service.scales(),
      years: this.canEdit() ? this.yearsApi.list() : of([] as AcademicYear[]),
    }).pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (r) => { this.scheme.set(r.scheme); this.templates.set(r.templates); this.scales.set(r.scales); this.years.set(r.years.filter((y) => (y.statusName ?? 'Active') === 'Active')); },
      error: () => this.failed.set(true),
    });
  }

  protected update(s: Scheme): void { this.scheme.set(s); }
  protected created(s: Scheme): void { void this.router.navigate(['/examinations/schemes', s.schemeId]); }
}
