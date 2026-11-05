import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { formatInr } from '../../../../core/utils/currency';
import { ButtonComponent, CardComponent } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { StudentMyFees } from '../../models/student.model';
import { StudentService } from '../../services/student.service';

/**
 * A Student's own fees, read-only (GET /api/Student/me/fees): totals, fee heads, every instalment with its status, and the payments made.
 * No pay button and nothing to edit. One component for the My Fees page and the Fees tab of My Profile.
 */
@Component({
  selector: 'app-student-my-fees',
  imports: [ButtonComponent, CardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './student-my-fees.component.scss',
  templateUrl: './student-my-fees.component.html',
})
export class StudentMyFeesComponent implements OnInit {
  private readonly students = inject(StudentService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly fees = signal<StudentMyFees | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  protected readonly inr = formatInr;
  protected readonly fmt = formatDate;
  protected readonly paidOn = (iso: string) => new Date(iso).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  /** The status pill colour, same words as the staff screens. */
  protected readonly tone = (status: string) => ({ Paid: 'green', 'Partially Paid': 'yellow', Pending: 'gray', Overdue: 'red' } as Record<string, string>)[status] ?? 'gray';
  protected readonly hasConcession = computed(() => (this.fees()?.heads ?? []).some((h) => h.concession > 0));

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.students
      .myFees()
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (f) => this.fees.set(f), error: () => this.loadFailed.set(true) }); // already toasted by the interceptor
  }
}
