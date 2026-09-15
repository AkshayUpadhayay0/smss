import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { CardComponent } from '../../shared/components/card/card.component';
import { IconComponent } from '../../shared/components/icon/icon.component';
import { ButtonComponent } from '../../shared/components/button/button.component';
import { TableComponent } from '../../shared/components/table/table.component';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';
import { TABLE_MOCK_ROWS } from '../../core/mock';
import { SortDirection, TableColumn, UserTableRow } from '../../core/models';
import { ConfirmDialogService, ToastService } from '../../core/services';

const COLUMNS: TableColumn<UserTableRow>[] = [
  { key: 'id', label: 'ID', sortable: true, width: '110px' },
  { key: 'name', label: 'Name', sortable: true },
  { key: 'email', label: 'Email', sortable: true },
  { key: 'organization', label: 'Organization', sortable: true },
  { key: 'role', label: 'Role', sortable: true },
  { key: 'status', label: 'Status', sortable: true },
  { key: 'createdDate', label: 'Created Date', sortable: true },
];

@Component({
  selector: 'app-table-page',
  standalone: true,
  imports: [
    PageHeaderComponent,
    CardComponent,
    IconComponent,
    ButtonComponent,
    TableComponent,
    PaginationComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './table.component.html',
  styleUrl: './table.component.scss',
})
export class TablePageComponent {
  private readonly confirmDialogService = inject(ConfirmDialogService);
  private readonly toastService = inject(ToastService);

  readonly columns = COLUMNS;
  readonly loading = signal(false);
  readonly searchTerm = signal('');
  readonly sortKey = signal<string>('name');
  readonly sortDirection = signal<SortDirection>('asc');
  readonly currentPage = signal(1);
  readonly pageSize = signal(10);

  readonly allRows = signal<UserTableRow[]>(TABLE_MOCK_ROWS);

  readonly filteredRows = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    let rows = this.allRows();

    if (term) {
      rows = rows.filter(
        (r) =>
          r.name.toLowerCase().includes(term) ||
          r.email.toLowerCase().includes(term) ||
          r.organization.toLowerCase().includes(term) ||
          r.role.toLowerCase().includes(term) ||
          r.status.toLowerCase().includes(term),
      );
    }

    const key = this.sortKey() as keyof UserTableRow;
    const dir = this.sortDirection();
    if (key && dir) {
      rows = [...rows].sort((a, b) => {
        const aVal = String(a[key]);
        const bVal = String(b[key]);
        return dir === 'asc' ? aVal.localeCompare(bVal) : bVal.localeCompare(aVal);
      });
    }

    return rows;
  });

  readonly pagedRows = computed(() => {
    const start = (this.currentPage() - 1) * this.pageSize();
    return this.filteredRows().slice(start, start + this.pageSize());
  });

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

  simulateLoading(): void {
    this.loading.set(true);
    setTimeout(() => this.loading.set(false), 900);
  }

  statusVariant(status: UserTableRow['status']): 'success' | 'neutral' | 'warning' {
    if (status === 'Active') return 'success';
    if (status === 'Pending') return 'warning';
    return 'neutral';
  }

  view(row: UserTableRow): void {
    this.toastService.info('Viewing user', `${row.name} (${row.email})`);
  }

  edit(row: UserTableRow): void {
    this.toastService.info('Edit user', `Editing ${row.name} — hook this up to your edit form.`);
  }

  async remove(row: UserTableRow): Promise<void> {
    const confirmed = await this.confirmDialogService.confirm({
      title: 'Delete user',
      message: `Are you sure you want to delete ${row.name}? This action cannot be undone.`,
      confirmLabel: 'Delete',
      variant: 'danger',
    });

    if (!confirmed) return;

    this.allRows.update((rows) => rows.filter((r) => r.id !== row.id));
    this.toastService.success('User deleted', `${row.name} was removed.`);
  }
}
