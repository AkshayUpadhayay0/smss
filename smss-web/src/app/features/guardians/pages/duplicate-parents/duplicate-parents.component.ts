import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { Breadcrumb, ButtonComponent, CardComponent, IconComponent, PageHeaderComponent } from '../../../../shared/components';
import { MergeDialogComponent } from '../../components/merge-dialog/merge-dialog.component';
import { DuplicateGroup, DuplicateReport } from '../../models/guardian.model';
import { GuardianService } from '../../services/guardian.service';

const MATCH_LABEL: Record<DuplicateGroup['nameMatch'], string> = { SAME: 'Same name', SIMILAR: 'Similar name', DIFFERENT: 'Different names' };

/**
 * Parents whose records share a mobile number (within this school), with how alike their names are and their students. Nothing is merged automatically: the admin picks the record to KEEP (the oldest is
 * pre-selected), opens the merge on another record of the group, reads the preview and confirms. A shared phone with different names may be two people (say, a parent and a relative): the flag says so.
 * School Admin and Principal only.
 */
@Component({
  selector: 'app-duplicate-parents',
  imports: [ButtonComponent, CardComponent, IconComponent, MergeDialogComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './duplicate-parents.component.scss',
  template: `
    <app-page-header title="Duplicate parents" subtitle="Parent records that share a mobile number" icon="users" [breadcrumbs]="breadcrumbs" />

    @if (loading()) {
      <app-card><p class="state" role="status">Loading…</p></app-card>
    } @else if (loadFailed()) {
      <app-card><p class="state" role="alert">The report could not be loaded. <app-button variant="secondary" size="sm" (click)="load()">Try again</app-button></p></app-card>
    } @else if (report().groups.length === 0) {
      <app-card><p class="state" data-state="empty">No two parent records share a mobile number.</p></app-card>
    } @else {
      <p class="count" data-count="groups">{{ report().groupCount }} {{ report().groupCount === 1 ? 'group' : 'groups' }} · {{ report().guardianCount }} records</p>
      @for (g of report().groups; track g.phoneKey) {
        <app-card>
          <div class="head" [attr.data-group]="g.phoneKey">
            <strong>{{ g.guardians[0].mobileNumber }}</strong>
            <span class="badge" [class]="'badge ' + g.nameMatch.toLowerCase()" data-badge>{{ matchLabel(g.nameMatch) }}</span>
          </div>
          @for (r of g.guardians; track r.guardianId) {
            <div class="rec" [attr.data-guardian]="r.guardianId">
              <label class="keep"><input type="radio" [name]="'keep-' + g.phoneKey" [checked]="survivorOf(g) === r.guardianId" (change)="keep(g, r.guardianId)" /> Keep this record{{ r.isOldest ? ' (oldest)' : '' }}</label>
              <div class="who">
                <strong>{{ r.name }}</strong> <span class="meta">{{ r.relationship }} · {{ r.hasWhatsApp ? 'WhatsApp' : 'no WhatsApp' }} · {{ r.hasEmail ? 'email' : 'no email' }}{{ r.hasLogin ? ' · has a login' : '' }}</span>
                @for (s of r.students; track s.studentId) {
                  <div class="meta">{{ s.studentName }} · {{ s.admissionNumber }}{{ s.className ? ' · ' + s.className + (s.sectionName ? ' – ' + s.sectionName : '') : '' }}{{ s.isPrimary ? '' : ' · not primary' }}</div>
                }
              </div>
              @if (survivorOf(g) !== r.guardianId) {
                <app-button variant="secondary" size="sm" (click)="open(g, r.guardianId)" data-action="merge-into"><app-icon name="users" [size]="14" />Merge into the kept record…</app-button>
              }
            </div>
          }
        </app-card>
      }
    }

    @if (merging(); as m) {
      <app-merge-dialog [survivorId]="m.survivorId" [absorbedId]="m.absorbedId" (merged)="onMerged()" (closed)="merging.set(null)" />
    }
  `,
})
export class DuplicateParentsComponent implements OnInit {
  private readonly service = inject(GuardianService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs: Breadcrumb[] = [{ label: 'Complete parent details', link: '/parent-details' }, { label: 'Duplicate parents' }];
  protected readonly report = signal<DuplicateReport>({ groupCount: 0, guardianCount: 0, groups: [] });
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly merging = signal<{ survivorId: number; absorbedId: number } | null>(null);
  /** The record chosen to keep, per group (the oldest until the admin picks another). */
  private readonly keepChoice = signal<Record<string, number>>({});

  ngOnInit(): void {
    this.load();
  }

  protected matchLabel(m: DuplicateGroup['nameMatch']): string {
    return MATCH_LABEL[m];
  }

  protected survivorOf(g: DuplicateGroup): number {
    return this.keepChoice()[g.phoneKey] ?? g.guardians.find((x) => x.isOldest)?.guardianId ?? g.guardians[0].guardianId;
  }

  protected keep(g: DuplicateGroup, guardianId: number): void {
    this.keepChoice.update((c) => ({ ...c, [g.phoneKey]: guardianId }));
  }

  protected open(g: DuplicateGroup, absorbedId: number): void {
    this.merging.set({ survivorId: this.survivorOf(g), absorbedId });
  }

  protected onMerged(): void {
    this.merging.set(null);
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .duplicates()
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (r) => this.report.set(r), error: () => this.loadFailed.set(true) });
  }
}
