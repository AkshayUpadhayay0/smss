import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, TableColumn, TableComponent } from '../../../../shared/components';
import { MyClass, MyClassSection, MyClassStudentRow } from '../../models/student.model';
import { StudentService } from '../../services/student.service';

const COLUMNS: TableColumn<MyClassStudentRow>[] = [
  { key: 'photoUrl', label: '', type: 'avatar', width: '64px', avatarLabel: (r) => r.fullName },
  { key: 'rollNumber', label: 'Roll Number', width: '130px' },
  { key: 'admissionNumber', label: 'Admission Number' },
  { key: 'fullName', label: 'Name' },
  { key: 'genderName', label: 'Gender' },
  { key: 'statusName', label: 'Status', type: 'status', width: '120px' },
];

interface SectionView {
  section: MyClassSection;
  title: string;
  rows: MyClassStudentRow[];
}

/**
 * "My Students" (Teacher): the sections the teacher is the class teacher of, then the sections they teach a subject in, each with its students (GET /api/Student/my-class).
 * Read-only: a row opens the restricted profile (/my-students/:studentId). No fee, login or edit actions exist on this screen.
 */
@Component({
  selector: 'app-my-students',
  imports: [ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, TableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './my-students.component.scss',
  templateUrl: './my-students.component.html',
})
export class MyStudentsComponent implements OnInit {
  private readonly students = inject(StudentService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'My Students' }];
  protected readonly data = signal<MyClass | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  protected readonly subtitle = computed(() => {
    const d = this.data();
    return d ? `Students in your classes, ${d.academicYearName}` : 'Students in your classes';
  });

  protected readonly views = computed<SectionView[]>(() =>
    (this.data()?.sections ?? []).map((section) => ({
      section,
      title: `${section.className} – ${section.sectionName}`,
      rows: section.students.map((s) => ({
        studentId: s.studentId,
        photoUrl: s.profilePhotoUrl ?? '',
        rollNumber: s.rollNumber ?? '—',
        admissionNumber: s.admissionNumber,
        fullName: s.fullName,
        genderName: s.genderName ?? '—',
        statusName: s.statusName,
      })),
    })),
  );

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.students
      .myClass()
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (d) => this.data.set(d), error: () => this.loadFailed.set(true) }); // already toasted by the interceptor
  }

  protected view(row: MyClassStudentRow): void {
    void this.router.navigate(['/my-students', row.studentId]);
  }
}
