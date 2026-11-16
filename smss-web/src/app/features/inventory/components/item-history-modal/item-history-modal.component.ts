import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ButtonComponent, ModalComponent, PaginationComponent, TableColumn, TableComponent } from '../../../../shared/components';
import { InvItem, InvMovement, formatDateTime, formatQty, movementLabel, reasonLabel } from '../../models/inventory.model';
import { InventoryService } from '../../services/inventory.service';

interface HistoryRow extends Record<string, unknown> { when: string; what: string; change: string; after: string; remarks: string; by: string }

const COLUMNS: TableColumn<HistoryRow>[] = [
  { key: 'when', label: 'When' },
  { key: 'what', label: 'Movement' },
  { key: 'change', label: 'Change' },
  { key: 'after', label: 'Balance' },
  { key: 'remarks', label: 'Remarks' },
  { key: 'by', label: 'By' },
];

/** One item's ledger, newest first. Read-only: the ledger is never edited (a correction is a new adjustment). */
@Component({
  selector: 'app-item-history-modal',
  imports: [ButtonComponent, ModalComponent, PaginationComponent, TableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../../styles/inventory-page.scss',
  template: `
    <app-modal [title]="'Stock history: ' + item().itemName" (closed)="closed.emit()">
      <p class="note">On hand now: <strong>{{ qty(item().currentQuantity) }} {{ item().unitCode }}</strong>. Every change to this number is listed here and can not be edited.</p>
      <app-table [columns]="columns" [rows]="rows()" [loading]="loading()" [emptyMessage]="failed() ? 'Could not load the history.' : 'No stock has moved on this item yet.'" />
      @if (failed()) {
        <div class="retry"><app-button variant="secondary" size="sm" (click)="load()">Try again</app-button></div>
      }
      <app-pagination [currentPage]="page()" [totalItems]="total()" [pageSize]="pageSize()" [pageSizeOptions]="[10, 25, 50]" (pageChange)="onPage($event)" (pageSizeChange)="onPageSize($event)" />
      <ng-container modal-footer><app-button variant="secondary" (click)="closed.emit()">Close</app-button></ng-container>
    </app-modal>
  `,
})
export class ItemHistoryModalComponent implements OnInit {
  private readonly service = inject(InventoryService);

  readonly item = input.required<InvItem>();
  readonly closed = output<void>();

  protected readonly columns = COLUMNS;
  protected readonly qty = formatQty;
  protected readonly movements = signal<InvMovement[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  protected readonly rows = computed<HistoryRow[]>(() =>
    this.movements().map((m) => ({
      when: formatDateTime(m.createdAt),
      what: m.movementType === 'ADJUSTMENT' && m.reasonCode ? `${movementLabel(m.movementType)}: ${reasonLabel(m.reasonCode)}` : movementLabel(m.movementType),
      change: `${m.quantityChange > 0 ? '+' : ''}${formatQty(m.quantityChange)} ${m.unitCode}`,
      after: `${formatQty(m.quantityAfter)} ${m.unitCode}`,
      remarks: m.remarks ?? '',
      by: m.createdByName ?? '',
    })),
  );

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.service
      .movements(this.item().itemId, this.page(), this.pageSize())
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({ next: (p) => { this.movements.set(p.items); this.total.set(p.totalCount); }, error: () => this.failed.set(true) });
  }

  protected onPage(p: number): void { this.page.set(p); this.load(); }
  protected onPageSize(s: number): void { this.pageSize.set(s); this.page.set(1); this.load(); }
}
