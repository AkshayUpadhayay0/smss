import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { catchError, finalize, of } from 'rxjs';
import { MasterDataService } from '../../../../core/services/master-data.service';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent } from '../../../../shared/components';
import { AdmissionActionsComponent } from '../../components/admission-actions/admission-actions.component';
import { AdmissionDocumentsComponent } from '../../components/admission-documents/admission-documents.component';
import { AdmissionDetail, AdmissionStatus, DOCUMENT_LOCKED_STATUSES, EDITABLE_STATUSES, GUARDIAN_RELATIONSHIPS, STATUS_META, formatDate } from '../../models/admission.model';
import { AdmissionService } from '../../services/admission.service';

interface Step {
  label: string;
  /** done = reached, current = where it is now, upcoming = not reached, failed = a rejected/cancelled exit */
  state: 'done' | 'current' | 'upcoming' | 'failed';
  date?: string | null;
}

@Component({
  selector: 'app-admission-detail',
  imports: [ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, AdmissionActionsComponent, AdmissionDocumentsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './admission-detail.component.scss',
  templateUrl: './admission-detail.component.html',
})
export class AdmissionDetailComponent implements OnInit {
  private readonly service = inject(AdmissionService);
  private readonly master = inject(MasterDataService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  /** Bound from the :admissionId route param (withComponentInputBinding). */
  readonly admissionId = input.required<string>();

  protected readonly admission = signal<AdmissionDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  // Location names are resolved from the cached lookups (the server stores ids only)
  protected readonly countryName = signal('');
  protected readonly stateName = signal('');
  protected readonly districtName = signal('');
  protected readonly cityName = signal('');

  protected readonly breadcrumbs = computed(() => [{ label: 'Admissions', link: '/admissions' }, { label: this.admission()?.applicationNumber ?? 'Application' }]);
  protected readonly statusMeta = computed(() => {
    const s = this.admission()?.statusName;
    return s ? STATUS_META[s] : null;
  });
  /** Applicant details can be edited only before a decision. */
  protected readonly canEdit = computed(() => EDITABLE_STATUSES.includes(this.admission()?.statusName as AdmissionStatus));
  protected readonly documentsLocked = computed(() => DOCUMENT_LOCKED_STATUSES.includes(this.admission()?.statusName as AdmissionStatus));
  protected readonly guardianRelationship = computed(() => {
    const r = this.admission()?.guardianRelationship;
    return GUARDIAN_RELATIONSHIPS.find((g) => g.value === r)?.label ?? r ?? '';
  });
  protected readonly address = computed(() => {
    const a = this.admission();
    if (!a) return '';
    return [a.addressLine1, a.addressLine2, this.cityName(), this.districtName(), this.stateName(), this.countryName(), a.pincode].filter((p) => !!p).join(', ');
  });
  protected readonly remarkText = computed(() => {
    const a = this.admission();
    if (!a) return null;
    if (a.statusName === 'REJECTED') return { label: 'Rejection reason', text: a.rejectionReason };
    return a.decisionRemarks ? { label: a.statusName === 'CANCELLED' ? 'Cancellation remarks' : 'Decision remarks', text: a.decisionRemarks } : null;
  });

  /** Submitted → Under Review → Approved → Admitted, or ending early at Rejected / Cancelled. */
  protected readonly steps = computed<Step[]>(() => {
    const a = this.admission();
    if (!a) return [];
    const s: AdmissionStatus = a.statusName;
    const order: AdmissionStatus[] = ['APPLICATION_SUBMITTED', 'UNDER_REVIEW', 'APPROVED', 'ADMITTED'];
    const dateFor: Record<string, string | null | undefined> = {
      APPLICATION_SUBMITTED: a.applicationDate,
      UNDER_REVIEW: a.reviewedAt,
      APPROVED: a.approvedAt,
      ADMITTED: s === 'ADMITTED' ? a.updatedAt : null,
    };

    if (s === 'REJECTED' || s === 'CANCELLED') {
      // only the steps the application actually passed through, then the failed exit
      const passed = order.filter((o) => o === 'APPLICATION_SUBMITTED' || (o === 'UNDER_REVIEW' && !!a.reviewedAt) || (o === 'APPROVED' && !!a.approvedAt));
      return [
        ...passed.map((o): Step => ({ label: STATUS_META[o].label, state: 'done', date: dateFor[o] })),
        { label: STATUS_META[s].label, state: 'failed', date: a.updatedAt },
      ];
    }
    const at = order.indexOf(s);
    return order.map((o, i): Step => ({
      label: STATUS_META[o].label,
      state: i < at ? 'done' : i === at ? (s === 'ADMITTED' ? 'done' : 'current') : 'upcoming',
      date: i <= at ? dateFor[o] : null,
    }));
  });

  protected readonly fmt = formatDate;

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    const id = Number(this.admissionId());
    if (!Number.isFinite(id)) {
      this.loadFailed.set(true);
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .getById(id)
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (a) => {
          this.admission.set(a);
          this.resolveLocation(a);
        },
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  protected onChanged(updated: AdmissionDetail): void {
    this.admission.set(updated);
  }

  protected back(): void {
    void this.router.navigate(['/admissions']);
  }

  protected edit(): void {
    void this.router.navigate(['/admissions', this.admission()?.admissionId, 'edit']);
  }

  private resolveLocation(a: AdmissionDetail): void {
    const find = (options: { label: string; value: string }[], id?: number | null) => options.find((o) => o.value === String(id))?.label ?? '';
    const safe = <T>(o: import('rxjs').Observable<T[]>) => o.pipe(catchError(() => of([] as T[])));

    if (a.countryId) safe(this.master.getCountryOptions()).subscribe((o) => this.countryName.set(find(o, a.countryId)));
    if (a.countryId && a.stateId) safe(this.master.getStateOptions(a.countryId)).subscribe((o) => this.stateName.set(find(o, a.stateId)));
    if (a.countryId && a.stateId && a.districtId)
      safe(this.master.getDistrictOptions(a.countryId, a.stateId)).subscribe((o) => this.districtName.set(find(o, a.districtId)));
    if (a.countryId && a.stateId && a.districtId && a.cityId)
      safe(this.master.getCityOptions(a.countryId, a.stateId, a.districtId)).subscribe((o) => this.cityName.set(find(o, a.cityId)));
  }
}
