import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { Breadcrumb, ButtonComponent, CardComponent, IconComponent, InputComponent, ModalComponent, PageHeaderComponent } from '../../../../shared/components';
import { StudentDocumentsComponent } from '../../components/student-documents/student-documents.component';
import { PendingStudentDocumentsRow } from '../../models/student-document.model';
import { StudentDocumentService } from '../../services/student-document.service';

/**
 * Students who are missing a required document, and what each one lacks (the list behind the "pending documents" tile of the School Admin and Principal dashboards).
 * School Admin, Principal and Admission Staff open it; Manage opens the student's documents to upload, waive or void. Documents never block anything: this is only a to-do list.
 */
@Component({
  selector: 'app-pending-documents',
  imports: [ButtonComponent, CardComponent, IconComponent, InputComponent, ModalComponent, PageHeaderComponent, StudentDocumentsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './pending-documents.component.scss',
  template: `
    <app-page-header title="Pending documents" subtitle="Students who have not yet submitted a required document" icon="file-text" [breadcrumbs]="breadcrumbs" />

    <app-card [noPadding]="true">
      <div class="toolbar">
        <div class="search">
          <app-input placeholder="Search by name or admission number…" prefixIcon="search" (input)="onSearch($event)" />
        </div>
        <span class="count" data-count="pending">{{ total() }} {{ total() === 1 ? 'student' : 'students' }} with pending documents</span>
      </div>

      @if (loading()) {
        <p class="state" role="status">Loading…</p>
      } @else if (loadFailed()) {
        <p class="state" role="alert">The list could not be loaded. <app-button variant="secondary" size="sm" (click)="load()">Try again</app-button></p>
      } @else if (visible().length === 0) {
        <p class="state" data-state="empty">{{ search() ? 'Nothing matches your search.' : 'No student has a pending document.' }}</p>
      } @else {
        <table class="list" aria-label="Students with pending documents">
          <thead>
            <tr><th>Student</th><th>Class</th><th>Documents not submitted</th><th></th></tr>
          </thead>
          <tbody>
            @for (s of visible(); track s.studentId) {
              <tr [attr.data-student]="s.studentId">
                <td><strong>{{ s.fullName }}</strong><br /><span class="meta">{{ s.admissionNumber }}</span></td>
                <td>{{ s.className ? s.className + (s.sectionName ? ' – ' + s.sectionName : '') : '—' }}</td>
                <td>
                  @for (m of s.missing; track m) {
                    <span class="chip">{{ m }}</span>
                  }
                </td>
                <td class="act"><app-button variant="secondary" size="sm" (click)="manage(s)"><app-icon name="file-text" [size]="14" />Manage</app-button></td>
              </tr>
            }
          </tbody>
        </table>
      }
    </app-card>

    @if (managing(); as m) {
      <app-modal [title]="'Documents · ' + m.fullName" (closed)="closeManage()">
        <app-student-documents [studentId]="m.studentId" />
      </app-modal>
    }
  `,
})
export class PendingDocumentsComponent implements OnInit {
  private readonly service = inject(StudentDocumentService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs: Breadcrumb[] = [{ label: 'Students', link: '/students' }, { label: 'Pending documents' }];
  protected readonly rows = signal<PendingStudentDocumentsRow[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly search = signal('');
  protected readonly managing = signal<PendingStudentDocumentsRow | null>(null);

  protected readonly visible = computed(() => {
    const term = this.search().trim().toLowerCase();
    return term ? this.rows().filter((r) => r.fullName.toLowerCase().includes(term) || r.admissionNumber.toLowerCase().includes(term) || r.studentId.toLowerCase().includes(term)) : this.rows();
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .pending()
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (d) => {
          this.rows.set(d.students);
          this.total.set(d.totalStudents);
        },
        error: () => this.loadFailed.set(true),
      });
  }

  protected onSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value ?? '');
  }

  protected manage(row: PendingStudentDocumentsRow): void {
    this.managing.set(row);
  }

  /** The list is read again after the documents of a student were changed (they may no longer be pending). */
  protected closeManage(): void {
    this.managing.set(null);
    this.load();
  }
}
