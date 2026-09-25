import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router, RouterModule } from '@angular/router';

import { PageHeaderComponent } from '../../../../shared/components/page-header/page-header.component';
import { CardComponent } from '../../../../shared/components/card/card.component';
import { IconComponent } from '../../../../shared/components/icon/icon.component';
import { ButtonComponent } from '../../../../shared/components/button/button.component';
import { TableComponent } from '../../../../shared/components/table/table.component';
import { PaginationComponent } from '../../../../shared/components/pagination/pagination.component';

import { SortDirection, TableColumn } from '../../../../core/models';
import { ConfirmDialogService, ToastService } from '../../../../core/services';

import { SchoolService } from '../../services/school.service';
import { SchoolsListModel } from '../../model/school.model';

const COLUMNS: TableColumn<SchoolsListModel>[] = [
  { key: 'schoolCode', label: 'School Code', sortable: true, width: '140px' },
  { key: 'schoolName', label: 'School Name', sortable: true },
  { key: 'email', label: 'Email', sortable: true },
  { key: 'mobileNumber', label: 'Mobile', sortable: true, width: '140px' },
  { key: 'schoolStatusId', label: 'Status', sortable: true, width: '110px' },
];

@Component({
  selector: 'app-school-list',
  standalone: true,
  imports: [
    RouterModule,
    PageHeaderComponent,
    CardComponent,
    IconComponent,
    ButtonComponent,
    TableComponent,
    PaginationComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './school-list.component.html',
  styleUrls: ['./school-list.component.scss'],
})
export class SchoolListComponent implements OnInit {
  private readonly schoolService = inject(SchoolService);
  private readonly confirmDialogService = inject(ConfirmDialogService);
  private readonly toastService = inject(ToastService);
  private readonly router = inject(Router);

  readonly columns = COLUMNS;

  readonly loading = signal(false);
  readonly searchTerm = signal('');
  readonly sortKey = signal<string>('schoolName');
  readonly sortDirection = signal<SortDirection>('asc');
  readonly currentPage = signal(1);
  readonly pageSize = signal(10);

  readonly allRows = signal<SchoolsListModel[]>([]);

  readonly filteredRows = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    let rows = this.allRows();

    if (term) {
      rows = rows.filter((r) =>
        [r.schoolCode, r.schoolName, r.email, r.mobileNumber].some((v) =>
          (v ?? '').toLowerCase().includes(term),
        ),
      );
    }

    const key = this.sortKey() as keyof SchoolsListModel;
    const dir = this.sortDirection();
    if (key && dir) {
      rows = [...rows].sort((a, b) => {
        const aVal = String(a[key] ?? '');
        const bVal = String(b[key] ?? '');
        return dir === 'asc' ? aVal.localeCompare(bVal) : bVal.localeCompare(aVal);
      });
    }

    return rows;
  });

  readonly pagedRows = computed(() => {
    const start = (this.currentPage() - 1) * this.pageSize();
    return this.filteredRows().slice(start, start + this.pageSize());
  });

  ngOnInit(): void {
    this.loadSchools();
  }

  loadSchools(): void {
    this.loading.set(true);

    this.schoolService.getSchools().subscribe({
      next: (res) => {
        this.loading.set(false);
        if (res.status) {
          this.allRows.set(res.data || []);
        } else if (res.statusCode !== 404) {
          this.toastService.danger('Failed to load schools', res.message);
        }
      },
      error: () => {
        this.loading.set(false);
        this.toastService.danger('Failed to load schools', 'Please try again.');
      },
    });
  }

  onSearch(value: string): void {
    this.searchTerm.set(value);
    this.currentPage.set(1);
  }

  onSortChange(key: string): void {
    if (this.sortKey() === key) {
      this.sortDirection.set(this.sortDirection() === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortKey.set(key);
      this.sortDirection.set('asc');
    }
  }

  onPageChange(page: number): void {
    this.currentPage.set(page);
  }

  onPageSizeChange(size: number): void {
    this.pageSize.set(size);
    this.currentPage.set(1);
  }

  view(row: SchoolsListModel): void {
    this.toastService.info('School details', `${row.schoolName} (${row.schoolCode})`);
  }

  edit(row: SchoolsListModel): void {
    this.router.navigate(['/schools', row.schoolId, 'edit']);
  }

  async remove(row: SchoolsListModel): Promise<void> {
    const confirmed = await this.confirmDialogService.confirm({
      title: 'Delete school',
      message: `Are you sure you want to delete ${row.schoolName}? This action cannot be undone.`,
      confirmLabel: 'Delete',
      variant: 'danger',
    });

    if (!confirmed) return;

    // No DELETE endpoint on the API yet — see note below.
    this.toastService.info('Not available yet', "Deleting schools isn't wired up to the API yet.");
  }
}