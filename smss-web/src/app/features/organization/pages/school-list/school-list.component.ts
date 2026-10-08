import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router, RouterModule } from '@angular/router';

import { PageHeaderComponent } from '../../../../shared/components/page-header/page-header.component';
import { CardComponent } from '../../../../shared/components/card/card.component';
import { IconComponent } from '../../../../shared/components/icon/icon.component';
import { ButtonComponent } from '../../../../shared/components/button/button.component';
import { TableComponent } from '../../../../shared/components/table/table.component';
import { TableCellDirective } from '../../../../shared/components/table/table-cell.directive';
import { BadgeComponent } from '../../../../shared/components/badge/badge.component';
import { PaginationComponent } from '../../../../shared/components/pagination/pagination.component';

import { SortDirection, TableColumn } from '../../../../core/models';
import { ConfirmDialogService, ToastService } from '../../../../core/services';
import { MasterDataService } from '../../../../core/services/master-data.service';

import { SchoolService } from '../../services/school.service';
import { SchoolsListModel } from '../../model/school.model';

// A school row plus a resolved, human-readable status name for display/search
type SchoolRow = SchoolsListModel & { schoolStatusName: string };

const COLUMNS: TableColumn<SchoolRow>[] = [
  { key: 'schoolCode', label: 'School Code', sortable: true, width: '140px' },
  { key: 'schoolName', label: 'School Name', sortable: true },
  { key: 'email', label: 'Email', sortable: true },
  { key: 'mobileNumber', label: 'Mobile', sortable: true, width: '140px' },
  { key: 'schoolStatusName', label: 'Status', sortable: true, width: '110px' },
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
    TableCellDirective,
    BadgeComponent,
    PaginationComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './school-list.component.html',
  styleUrls: ['./school-list.component.scss'],
})
export class SchoolListComponent implements OnInit {
  private readonly schoolService = inject(SchoolService);
  private readonly masterDataService = inject(MasterDataService);
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

  // sid -> name map for the "general status" group (Active / Inactive)
  private readonly statusNameById = signal<Record<number, string>>({});
  private activeStatusId: number | null = null;
  private inactiveStatusId: number | null = null;

  private readonly rawRows = signal<SchoolsListModel[]>([]);

  readonly allRows = computed<SchoolRow[]>(() => {
    const names = this.statusNameById();
    return this.rawRows().map((r) => ({
      ...r,
      schoolStatusName: r.schoolStatusId != null ? names[r.schoolStatusId] ?? String(r.schoolStatusId) : '-',
    }));
  });

  readonly filteredRows = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    let rows = this.allRows();

    if (term) {
      rows = rows.filter((r) =>
        [r.schoolCode, r.schoolName, r.email, r.mobileNumber, r.schoolStatusName].some((v) =>
          (v ?? '').toLowerCase().includes(term),
        ),
      );
    }

    const key = this.sortKey() as keyof SchoolRow;
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
    this.masterDataService.getStatusesByType('general status').subscribe((statuses) => {
      const map: Record<number, string> = {};
      statuses.forEach((s) => {
        map[s.sid] = s.sname;
        if (s.sname === 'Active') this.activeStatusId = s.sid;
        if (s.sname === 'Inactive') this.inactiveStatusId = s.sid;
      });
      this.statusNameById.set(map);
    });

    this.loadSchools();
  }

  loadSchools(): void {
    this.loading.set(true);

    this.schoolService.getSchools().subscribe({
      next: (res) => {
        this.loading.set(false);
        if (res.status) {
          this.rawRows.set(res.data || []);
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

  view(row: SchoolRow): void {
    this.toastService.info('School details', `${row.schoolName} (${row.schoolCode})`);
  }

  edit(row: SchoolRow): void {
    this.router.navigate(['/schools', row.schoolId, 'edit']);
  }

  isActive(row: SchoolRow): boolean {
    return this.activeStatusId != null && row.schoolStatusId === this.activeStatusId;
  }

  statusActionIcon(row: SchoolRow): string {
    return this.isActive(row) ? 'trash' : 'circle-check';
  }

  statusActionLabel(row: SchoolRow): string {
    return this.isActive(row) ? 'Deactivate' : 'Activate';
  }

  async toggleStatus(row: SchoolRow): Promise<void> {
    const willActivate = !this.isActive(row);
    const confirmed = await this.confirmDialogService.confirm({
      title: willActivate ? 'Activate school' : 'Deactivate school',
      message: willActivate
        ? `Reactivate ${row.schoolName}? Its login will be re-enabled.`
        : `Deactivate ${row.schoolName}? Its login will be disabled until reactivated.`,
      confirmLabel: willActivate ? 'Activate' : 'Deactivate',
      variant: willActivate ? 'primary' : 'danger',
    });

    if (!confirmed) return;

    this.schoolService.toggleSchoolStatus(row.schoolId).subscribe({
      next: (res) => {
        if (res.status && res.data) {
          this.rawRows.update((rows) =>
            rows.map((r) => (r.schoolId === row.schoolId ? { ...r, schoolStatusId: res.data!.schoolStatusId } : r)),
          );
          this.toastService.success(willActivate ? 'School activated' : 'School deactivated', res.message);
        } else {
          this.toastService.danger('Action failed', res.message);
        }
      },
      error: () => this.toastService.danger('Action failed', 'Please try again.'),
    });
  }
}