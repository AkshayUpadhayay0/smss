import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { toUploadUrl } from '../../../../core/utils/upload-url';
import { CardComponent, PageHeaderComponent } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { Employee, employmentStatusTone } from '../../models/employee.model';
import { EmployeeService } from '../../services/employee.service';

/**
 * "My Profile": the signed-in employee's OWN record (GET /api/Employee/me), read-only. No edit, photo, documents, status or login actions: those stay with the
 * School Admin on the Employees screens. A person changes their own password from the user menu; anything else on this record is corrected by the School Admin.
 */
@Component({
  selector: 'app-my-profile',
  imports: [CardComponent, PageHeaderComponent, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './my-profile.component.scss',
  templateUrl: './my-profile.component.html',
})
export class MyProfileComponent implements OnInit {
  private readonly service = inject(EmployeeService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly employee = signal<Employee | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  protected readonly fmt = formatDate;
  protected readonly tone = employmentStatusTone;
  protected readonly breadcrumbs = [{ label: 'My Profile' }];
  protected readonly photoUrl = computed(() => toUploadUrl(this.employee()?.photoUrl));
  protected readonly initials = computed(() =>
    (this.employee()?.fullName ?? '').split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]!.toUpperCase()).join('') || '?',
  );

  ngOnInit(): void {
    this.service
      .me()
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (e) => this.employee.set(e), error: () => this.loadFailed.set(true) }); // already toasted by the interceptor
  }
}
