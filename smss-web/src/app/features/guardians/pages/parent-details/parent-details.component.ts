import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ROLES } from '../../../../core/config/route-roles';
import { AuthService } from '../../../../core/services/auth.service';
import { catchError, finalize, of } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import {
  Breadcrumb,
  ButtonComponent,
  CardComponent,
  IconComponent,
  InputComponent,
  PageHeaderComponent,
  PaginationComponent,
  SelectComponent,
  SelectOption,
} from '../../../../shared/components';
import { SectionService } from '../../../school-setup/services/section.service';
import { Section } from '../../../school-setup/models/section.model';
import { GuardianEditDialogComponent } from '../../components/guardian-edit-dialog/guardian-edit-dialog.component';
import { IncompleteGuardian } from '../../models/guardian.model';
import { GuardianService } from '../../services/guardian.service';

/**
 * "Complete parent details": the primary guardians of ACTIVE students that still lack a WhatsApp number or an email. The class and section shown and filtered on are those of the enrolment in the school's
 * CURRENT academic year (a student not enrolled in it is still listed, with no class, and no class filter matches them). Each row opens the edit form. The bulk "Use mobile number as WhatsApp" works on
 * the TICKED rows of the page only (there is no "select all 355"), asks first, never touches an email and skips guardians that already have a WhatsApp number. Nothing here blocks any student.
 */
@Component({
  selector: 'app-parent-details',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, GuardianEditDialogComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './parent-details.component.scss',
  template: `
    <app-page-header title="Complete parent details" subtitle="Parents whose WhatsApp number or email is still missing" icon="users" [breadcrumbs]="breadcrumbs">
      @if (canMerge()) {
        <app-button variant="secondary" (click)="duplicates()" data-link="duplicates"><app-icon name="copy" [size]="16" />Duplicate parents</app-button>
      }
    </app-page-header>

    <app-card [noPadding]="true">
      <div class="toolbar">
        <div class="search">
          <app-input placeholder="Search by parent, student, admission no. or mobile…" prefixIcon="search" (input)="onSearch($event)" />
        </div>
        <div class="filter"><app-select placeholder="All classes" [options]="classOptions()" [formControl]="classControl" /></div>
        <div class="filter"><app-select placeholder="All sections" [options]="sectionOptions()" [formControl]="sectionControl" /></div>
        <span class="count" data-count="incomplete">{{ total() }} {{ total() === 1 ? 'parent' : 'parents' }}{{ filtered() ? ' match' : ' to complete' }}</span>
      </div>
      <p class="note" data-note="year">Class and section are from the school's current academic year.</p>

      <div class="bulk">
        <app-button variant="secondary" size="sm" [disabled]="selectedCount() === 0 || busy()" (click)="useMobile()" data-action="bulk">
          <app-icon name="smartphone" [size]="14" />Use mobile number as WhatsApp ({{ selectedCount() }})
        </app-button>
        <span class="hint">Tick the parents to change. Their email is not touched.</span>
      </div>

      @if (loading()) {
        <p class="state" role="status">Loading…</p>
      } @else if (loadFailed()) {
        <p class="state" role="alert">The list could not be loaded. <app-button variant="secondary" size="sm" (click)="load()">Try again</app-button></p>
      } @else if (items().length === 0) {
        <p class="state" data-state="empty">{{ filtered() ? 'Nothing matches your filters.' : 'Every parent has a WhatsApp number and an email.' }}</p>
      } @else {
        <table class="list" aria-label="Parents to complete">
          <thead>
            <tr>
              <th class="check"><input type="checkbox" [checked]="allOnPageSelected()" (change)="toggleAll($event)" aria-label="Select all parents on this page" data-check="page" /></th>
              <th>Parent</th><th>Mobile</th><th>Missing</th><th>Student</th><th></th>
            </tr>
          </thead>
          <tbody>
            @for (g of items(); track g.guardianId) {
              <tr [attr.data-guardian]="g.guardianId">
                <td class="check"><input type="checkbox" [checked]="isSelected(g.guardianId)" (change)="toggle(g.guardianId)" [attr.aria-label]="'Select ' + g.name" data-check="row" /></td>
                <td><strong>{{ g.name }}</strong><br /><span class="meta">{{ g.relationship }}</span></td>
                <td>{{ g.mobileNumber }}</td>
                <td>
                  @for (m of g.missing; track m) {
                    <span class="chip">{{ m }}</span>
                  }
                </td>
                <td>
                  @for (s of g.students; track s.studentId) {
                    <div>{{ s.studentName }}<span class="meta"> · {{ s.className ? s.className + (s.sectionName ? ' – ' + s.sectionName : '') : 'Not enrolled this year' }}</span></div>
                  }
                </td>
                <td class="act"><app-button variant="secondary" size="sm" (click)="edit(g)"><app-icon name="pencil" [size]="14" />Edit</app-button></td>
              </tr>
            }
          </tbody>
        </table>
      }

      <app-pagination [currentPage]="page()" [totalItems]="total()" [pageSize]="pageSize()" (pageChange)="goToPage($event)" (pageSizeChange)="onPageSize($event)" />
    </app-card>

    @if (editing(); as g) {
      <app-guardian-edit-dialog [guardianId]="g.guardianId" (saved)="onSaved()" (closed)="editing.set(null)" />
    }
  `,
})
export class ParentDetailsComponent implements OnInit {
  private readonly service = inject(GuardianService);
  private readonly sections = inject(SectionService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  /** The duplicate report and the merge are for the School Admin and the Principal only. */
  protected readonly canMerge = computed(() => this.auth.currentUserRoles().some((r) => r === ROLES.SCHOOL_ADMIN || r === ROLES.PRINCIPAL));
  protected duplicates(): void {
    void this.router.navigate(['/duplicate-parents']);
  }

  protected readonly breadcrumbs: Breadcrumb[] = [{ label: 'Students', link: '/students' }, { label: 'Complete parent details' }];
  protected readonly items = signal<IncompleteGuardian[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(25);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busy = signal(false);
  protected readonly search = signal('');
  protected readonly editing = signal<IncompleteGuardian | null>(null);
  /** The ticked guardians. Cleared whenever the page, the page size or a filter changes, so a bulk action can only ever act on rows that are on screen. */
  protected readonly selected = signal<ReadonlySet<number>>(new Set());
  protected readonly selectedCount = computed(() => this.selected().size);

  protected readonly classControl = new FormControl('', { nonNullable: true });
  protected readonly sectionControl = new FormControl('', { nonNullable: true });
  private readonly allSections = signal<Section[]>([]);
  /** The class control's value as a signal, so the section options follow it. */
  private readonly classValue = signal('');
  protected readonly classOptions = computed<SelectOption[]>(() => {
    const seen = new Map<number, string>();
    for (const s of this.allSections()) if (s.statusName === 'Active' || s.statusName == null) seen.set(s.classId, s.className);
    return [...seen.entries()].map(([value, label]) => ({ value: String(value), label })).sort((a, b) => a.label.localeCompare(b.label, undefined, { numeric: true }));
  });
  protected readonly sectionOptions = computed<SelectOption[]>(() => {
    const cls = this.classValue();
    return this.allSections().filter((s) => (!cls || String(s.classId) === cls) && (s.statusName === 'Active' || s.statusName == null)).map((s) => ({ value: String(s.sectionId), label: cls ? s.sectionName : `${s.className} – ${s.sectionName}` }));
  });
  protected readonly filtered = computed(() => !!this.search() || !!this.filterTick());
  private readonly filterTick = signal('');

  protected readonly allOnPageSelected = computed(() => this.items().length > 0 && this.items().every((g) => this.selected().has(g.guardianId)));

  ngOnInit(): void {
    this.sections.list().pipe(catchError(() => of([] as Section[])), takeUntilDestroyed(this.destroyRef)).subscribe((l) => this.allSections.set(l ?? []));
    this.classControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => {
      this.classValue.set(v);
      this.sectionControl.setValue('', { emitEvent: false });
      this.filterChanged();
    });
    this.sectionControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.filterChanged());
    this.load();
  }

  private filterChanged(): void {
    this.filterTick.set(this.classControl.value || this.sectionControl.value ? 'f' : '');
    this.page.set(1);
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.selected.set(new Set());
    this.service
      .incomplete({ classId: Number(this.classControl.value) || undefined, sectionId: Number(this.sectionControl.value) || undefined, search: this.search(), page: this.page(), pageSize: this.pageSize() })
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => {
          this.items.set(r.items);
          this.total.set(r.total);
        },
        error: () => this.loadFailed.set(true),
      });
  }

  protected onSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value ?? '');
    this.page.set(1);
    this.load();
  }

  protected goToPage(p: number): void {
    this.page.set(p);
    this.load();
  }

  protected onPageSize(size: number): void {
    this.pageSize.set(size);
    this.page.set(1);
    this.load();
  }

  protected isSelected(id: number): boolean {
    return this.selected().has(id);
  }

  protected toggle(id: number): void {
    const next = new Set(this.selected());
    if (!next.delete(id)) next.add(id);
    this.selected.set(next);
  }

  /** Select (or clear) the rows of THIS page only. */
  protected toggleAll(event: Event): void {
    const on = (event.target as HTMLInputElement).checked;
    this.selected.set(on ? new Set(this.items().map((g) => g.guardianId)) : new Set());
  }

  protected edit(g: IncompleteGuardian): void {
    this.editing.set(g);
  }

  protected onSaved(): void {
    this.editing.set(null);
    this.load();
  }

  protected async useMobile(): Promise<void> {
    const ids = [...this.selected()];
    if (ids.length === 0 || this.busy()) return;
    const ok = await this.confirmDialog.confirm({
      title: 'Use mobile number as WhatsApp?',
      message: `The mobile number of ${ids.length} selected ${ids.length === 1 ? 'parent' : 'parents'} will be saved as their WhatsApp number (with +91). Parents who already have a WhatsApp number are left as they are. Email is not changed. Every change is recorded.`,
      confirmText: `Use for ${ids.length}`,
    });
    if (!ok) return;
    this.busy.set(true);
    this.service
      .useMobileAsWhatsApp(ids)
      .pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => {
          this.toast.success(r.updated === 0 ? 'Every selected parent already had a WhatsApp number.' : `${r.updated} ${r.updated === 1 ? 'parent now uses' : 'parents now use'} their mobile number as WhatsApp.${r.skipped ? ` ${r.skipped} already had one.` : ''}`);
          this.load();
        },
        error: () => this.load(),   // the server reason is toasted; nothing was changed, show the truth
      });
  }
}
