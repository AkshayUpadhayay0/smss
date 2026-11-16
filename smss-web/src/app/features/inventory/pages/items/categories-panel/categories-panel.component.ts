import { ChangeDetectionStrategy, Component, OnInit, inject, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../../core/services/confirm-dialog.service';
import { ButtonComponent, CardComponent, IconComponent, TableColumn, TableComponent } from '../../../../../shared/components';
import { CategoryFormModalComponent } from '../../../components/category-form-modal/category-form-modal.component';
import { InvCategory } from '../../../models/inventory.model';
import { InventoryService } from '../../../services/inventory.service';

interface CategoryRow extends Record<string, unknown> { categoryId: number; categoryName: string; categoryCode: string; description: string; itemCount: number; statusName: string; isActive: boolean }

const COLUMNS: TableColumn<CategoryRow>[] = [
  { key: 'categoryName', label: 'Category' },
  { key: 'categoryCode', label: 'Code' },
  { key: 'description', label: 'Description' },
  { key: 'itemCount', label: 'Items' },
  { key: 'statusName', label: 'Status', type: 'status' },
];

/** The school's item categories (small list: no paging). Add / edit / (de)activate only when the server says the caller can manage. */
@Component({
  selector: 'app-categories-panel',
  imports: [ButtonComponent, CardComponent, CategoryFormModalComponent, IconComponent, TableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../../../styles/inventory-page.scss',
  template: `
    <app-card [noPadding]="true">
      <div class="toolbar">
        <span class="count">{{ categories().length }} {{ categories().length === 1 ? 'category' : 'categories' }}</span>
        @if (canManage()) {
          <app-button (click)="modal.set({ category: null })"><app-icon name="plus" [size]="16" />Add Category</app-button>
        }
      </div>
      <app-table
        [columns]="columns"
        [rows]="rows()"
        [loading]="loading()"
        [hasActions]="canManage()"
        [emptyMessage]="failed() ? 'Could not load the categories.' : 'No categories yet. Add one (for example Stationery) before adding items.'"
      >
        <ng-template #rowActions let-row>
          <app-button variant="secondary" size="sm" aria-label="Edit" title="Edit" (click)="edit(row)"><app-icon name="pencil" [size]="14" /></app-button>
          @if (row.isActive) {
            <app-button variant="danger-ghost" size="sm" [disabled]="busyId() === row.categoryId" (click)="toggle(row)"><app-icon name="toggle-left" [size]="14" />Deactivate</app-button>
          } @else {
            <app-button variant="secondary" size="sm" [disabled]="busyId() === row.categoryId" (click)="toggle(row)"><app-icon name="toggle-right" [size]="14" />Activate</app-button>
          }
        </ng-template>
      </app-table>
      @if (failed()) {
        <div class="retry"><app-button variant="secondary" size="sm" (click)="load()">Try again</app-button></div>
      }
    </app-card>

    @if (modal(); as m) {
      <app-category-form-modal [category]="m.category" (saved)="onSaved()" (closed)="modal.set(null)" />
    }
  `,
})
export class CategoriesPanelComponent implements OnInit {
  private readonly service = inject(InventoryService);
  private readonly confirmDialog = inject(ConfirmDialogService);

  /** Tells the page the list changed (its category filter and item form use it). */
  readonly changed = output<InvCategory[]>();

  protected readonly columns = COLUMNS;
  protected readonly categories = signal<InvCategory[]>([]);
  protected readonly canManageFromServer = signal(false);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly busyId = signal<number | null>(null);
  protected readonly modal = signal<{ category: InvCategory | null } | null>(null);

  protected canManage = () => this.canManageFromServer();
  protected rows = () =>
    this.categories().map<CategoryRow>((c) => ({
      categoryId: c.categoryId, categoryName: c.categoryName, categoryCode: c.categoryCode, description: c.description ?? '', itemCount: c.itemCount,
      statusName: c.isActive ? 'Active' : 'Inactive', isActive: c.isActive,
    }));

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.service
      .categories()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (r) => { this.canManageFromServer.set(r.canManage); this.categories.set(r.items); this.changed.emit(r.items); },
        error: () => this.failed.set(true),
      });
  }

  protected edit(row: CategoryRow): void {
    this.modal.set({ category: this.categories().find((c) => c.categoryId === row.categoryId) ?? null });
  }

  protected onSaved(): void {
    this.modal.set(null);
    this.load();   // item counts and the order are the server's to decide
  }

  protected async toggle(row: CategoryRow): Promise<void> {
    const deactivating = row.isActive;
    const ok = await this.confirmDialog.confirm({
      title: `${deactivating ? 'Deactivate' : 'Activate'} category?`,
      message: deactivating
        ? `"${row.categoryName}" can no longer be chosen for new items. Its ${row.itemCount} existing item(s) and their stock are not affected, and you can activate it again later.`
        : `"${row.categoryName}" can be chosen for new items again.`,
      confirmText: deactivating ? 'Deactivate' : 'Activate',
      variant: deactivating ? 'danger' : 'primary',
    });
    if (!ok) return;
    this.busyId.set(row.categoryId);
    this.service
      .toggleCategory(row.categoryId)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({ next: () => this.load(), error: () => undefined });
  }
}
