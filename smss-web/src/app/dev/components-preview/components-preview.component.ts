import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ConfirmDialogService } from '../../core/services/confirm-dialog.service';
import { ToastService } from '../../core/services/toast.service';
import {
  ButtonComponent,
  CardComponent,
  IconComponent,
  InputComponent,
  PageHeaderComponent,
  PaginationComponent,
  SelectComponent,
  SelectOption,
  SortChange,
  TableColumn,
  TableComponent,
  ICONS,
  IconName,
} from '../../shared/components';

interface DemoRow {
  code: string;
  name: string;
  city: string;
  status: string;
}

const DEMO_ROWS: DemoRow[] = Array.from({ length: 47 }, (_, i) => ({
  code: `SCH${String(i + 1).padStart(3, '0')}`,
  name: ['Green Valley Public School', 'St. Mary Academy', 'Sunrise International', 'Delhi Model School', 'Lotus Valley'][i % 5] + ` ${i + 1}`,
  city: ['Delhi', 'Mumbai', 'Pune', 'Jaipur', 'Lucknow'][i % 5],
  status: i % 4 === 3 ? 'Inactive' : 'Active',
}));

/** Throwaway page — delete together with the /dev route once the shell is approved. */
@Component({
  selector: 'app-components-preview',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, SelectComponent, TableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './components-preview.component.scss',
  templateUrl: './components-preview.component.html',
})
export class ComponentsPreviewComponent {
  private readonly toast = inject(ToastService);
  private readonly confirmDialog = inject(ConfirmDialogService);

  protected readonly iconNames = Object.keys(ICONS) as IconName[];

  protected readonly form = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email] }),
    board: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    levels: new FormControl<string[]>([], { nonNullable: true }),
  });
  protected readonly submitted = signal(false);

  protected readonly boardOptions: SelectOption[] = [
    { label: 'CBSE', value: (1).toString() },
    { label: 'ICSE', value: (2).toString() },
    { label: 'State Board', value: (3).toString() },
  ];

  protected readonly columns: TableColumn<DemoRow>[] = [
    { key: 'code', label: 'Code', sortable: true, width: '120px' },
    { key: 'name', label: 'School Name', sortable: true },
    { key: 'city', label: 'City', sortable: true },
    { key: 'status', label: 'Status', type: 'status' },
  ];

  protected readonly loading = signal(false);
  protected readonly showEmpty = signal(false);
  protected readonly search = signal('');
  protected readonly sortKey = signal<string | null>('code');
  protected readonly sortDirection = signal<'asc' | 'desc'>('asc');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);

  private readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const rows = this.showEmpty() ? [] : DEMO_ROWS.filter((r) => !q || r.name.toLowerCase().includes(q) || r.code.toLowerCase().includes(q));
    const key = this.sortKey() as keyof DemoRow | null;
    if (!key) return rows;
    const dir = this.sortDirection() === 'asc' ? 1 : -1;
    return [...rows].sort((a, b) => a[key].localeCompare(b[key]) * dir);
  });
  protected readonly total = computed(() => this.filtered().length);
  protected readonly pageRows = computed(() => {
    const start = (this.page() - 1) * this.pageSize();
    return this.filtered().slice(start, start + this.pageSize());
  });

  protected onSort(e: SortChange): void {
    this.sortKey.set(e.key);
    this.sortDirection.set(e.direction);
  }

  protected onSearch(e: Event): void {
    this.search.set((e.target as HTMLInputElement).value);
    this.page.set(1);
  }

  protected onPageSize(size: number): void {
    this.pageSize.set(size);
    this.page.set(1);
  }

  protected toggleLoading(): void {
    this.loading.update((v) => !v);
  }

  protected error(name: 'name' | 'email' | 'board'): string | undefined {
    const c = this.form.controls[name];
    if (!(c.touched || this.submitted()) || c.valid) return undefined;
    return c.hasError('email') ? 'Enter a valid email address' : 'This field is required';
  }

  protected submit(): void {
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.valid) this.toast.success('Form is valid', 'Saved');
    else this.toast.error('Please fix the highlighted fields');
  }

  protected showToast(type: 'success' | 'error' | 'info' | 'warning' | 'danger'): void {
    const messages = {
      success: 'School registered successfully.',
      error: 'Could not reach the server.',
      info: 'You have 3 unread notifications.',
      warning: 'Your session expires in 5 minutes.',
      danger: 'Record could not be deleted.',
    };
    this.toast[type](messages[type], type[0].toUpperCase() + type.slice(1));
  }

  protected async askConfirm(danger: boolean): Promise<void> {
    const ok = await this.confirmDialog.confirm({
      title: danger ? 'Deactivate school?' : 'Save changes?',
      message: danger ? 'The school will be marked Inactive and users can no longer log in.' : 'Your changes will be applied immediately.',
      confirmText: danger ? 'Deactivate' : 'Save',
      variant: danger ? 'danger' : 'primary',
    });
    this.toast.info(ok ? 'Confirmed' : 'Cancelled');
  }
}
