import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ButtonComponent, CardComponent, PageHeaderComponent } from '../../../../shared/components';
import { AdmissionStatus, STATUS_META, formatDate } from '../../../admissions/models/admission.model';
import { AdmissionDashboard } from '../../models/admission-dashboard.model';
import { AdmissionDashboardService } from '../../services/admission-dashboard.service';
import { buildAdmissionStats } from '../../utils/admission-dashboard-format';

/**
 * The Admission Staff /dashboard: applications by status, how many arrived this week, which open applications are missing a required document, and the last 10 applications,
 * with a link to Admissions. One request (GET /api/AdmissionDashboard/summary), read-only. A part the server could not load says "Unavailable" rather than showing 0.
 */
@Component({
  selector: 'app-admission-dashboard',
  imports: [RouterLink, ButtonComponent, CardComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../school-dashboard/school-dashboard.component.scss',
  templateUrl: './admission-dashboard.component.html',
})
export class AdmissionDashboardComponent implements OnInit {
  private readonly service = inject(AdmissionDashboardService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'Dashboard' }];
  protected readonly date = formatDate;

  protected readonly data = signal<AdmissionDashboard | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  protected readonly cards = computed(() => {
    const d = this.data();
    return d ? buildAdmissionStats(d) : [];
  });
  protected readonly applicationsDown = computed(() => this.data()?.partsUnavailable.includes('applications') ?? false);
  protected readonly documentsDown = computed(() => this.data()?.partsUnavailable.includes('documents') ?? false);

  protected statusLabel(status: string): string {
    return STATUS_META[status as AdmissionStatus]?.label ?? status;
  }
  protected statusTone(status: string): string {
    return STATUS_META[status as AdmissionStatus]?.tone ?? 'gray';
  }

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .summary()
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({ next: (d) => this.data.set(d), error: () => this.loadFailed.set(true) });
  }
}
