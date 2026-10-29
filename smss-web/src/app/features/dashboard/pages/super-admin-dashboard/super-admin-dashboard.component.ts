import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { formatInr } from '../../../../core/utils/currency';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { SuperAdminSummary } from '../../../subscriptions/models/super-admin-dashboard.model';
import { SuperAdminDashboardService } from '../../../subscriptions/services/super-admin-dashboard.service';

/**
 * The Super Admin's /dashboard: school management (A) and subscription & payments (B) at a glance. Numbers and panels only: the schools and
 * invoices tables live on /subscriptions and /invoices, and the stat cards link there (pre-filtered where a filter says the same thing).
 */
@Component({
  selector: 'app-super-admin-dashboard',
  imports: [RouterLink, ButtonComponent, CardComponent, IconComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './super-admin-dashboard.component.scss',
  templateUrl: './super-admin-dashboard.component.html',
})
export class SuperAdminDashboardComponent implements OnInit {
  private readonly service = inject(SuperAdminDashboardService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly inr = formatInr;
  protected readonly date = formatDate;

  protected readonly summary = signal<SuperAdminSummary | null>(null);
  protected readonly summaryFailed = signal(false);

  ngOnInit(): void {
    this.loadSummary();
  }

  protected loadSummary(): void {
    this.service
      .summary()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (s) => { this.summary.set(s); this.summaryFailed.set(false); }, error: () => this.summaryFailed.set(true) });
  }

  protected addSchool(): void { void this.router.navigate(['/schools/add']); }
  protected allSchools(): void { void this.router.navigate(['/schools']); }
}
