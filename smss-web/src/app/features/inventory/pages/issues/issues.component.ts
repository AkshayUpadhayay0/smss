import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, SelectComponent, SelectOption, TableColumn, TableComponent } from '../../../../shared/components';
import { InventoryTabsComponent } from '../../components/inventory-tabs/inventory-tabs.component';
import { IssueDetailModalComponent } from '../../components/issue-detail-modal/issue-detail-modal.component';
import { IssueFormModalComponent } from '../../components/issue-form-modal/issue-form-modal.component';
import { InvIssue, RECIPIENT_LABELS, formatDate, formatQty, issueTone } from '../../models/inventory.model';
import { InventoryService } from '../../services/inventory.service';

interface IssueRow extends Record<string, unknown> { issueId: number; issueNumber: string; issueDate: string; recipient: string; kind: string; items: string; returned: string; status: string; by: string }

const COLUMNS: TableColumn<IssueRow>[] = [
  { key: 'issueNumber', label: 'Issue no.' },
  { key: 'issueDate', label: 'Date' },
  { key: 'recipient', label: 'Issued to' },
  { key: 'kind', label: 'Type' },
  { key: 'items', label: 'Quantity' },
  { key: 'returned', label: 'Returned' },
  { key: 'status', label: 'Status', type: 'badge', tone: issueTone },
  { key: 'by', label: 'Issued by' },
];

const STATUS_FILTER: SelectOption[] = [
  { value: '', label: 'Any status' }, { value: 'Outstanding', label: 'Outstanding' }, { value: 'Partially Returned', label: 'Partially returned' },
  { value: 'Returned', label: 'Returned' }, { value: 'Voided', label: 'Voided' },
];
const TYPE_FILTER: SelectOption[] = [{ value: '', label: 'Anyone' }, ...Object.entries(RECIPIENT_LABELS).map(([value, label]) => ({ value, label }))];

/** Issues to staff, students and departments, and their returns. The status is derived by the server from what has come back. */
@Component({
  selector: 'app-inventory-issues',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, InputComponent, InventoryTabsComponent, IssueDetailModalComponent, IssueFormModalComponent, PageHeaderComponent, PaginationComponent, SelectComponent, TableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../../styles/inventory-page.scss',
  templateUrl: './issues.component.html',
})
export class InventoryIssuesComponent implements OnInit {
  private readonly service = inject(InventoryService);
  private readonly toast = inject(ToastService);

  protected readonly breadcrumbs = [{ label: 'Inventory' }, { label: 'Issue / Return' }];
  protected readonly columns = COLUMNS;
  protected readonly statusFilter = STATUS_FILTER;
  protected readonly typeFilter = TYPE_FILTER;

  protected readonly canManage = signal(false);
  protected readonly list = signal<InvIssue[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly creating = signal(false);
  protected readonly openId = signal<number | null>(null);

  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly status = new FormControl('', { nonNullable: true });
  protected readonly type = new FormControl('', { nonNullable: true });
  protected readonly from = new FormControl('', { nonNullable: true });
  protected readonly to = new FormControl('', { nonNullable: true });

  protected readonly rows = computed<IssueRow[]>(() =>
    this.list().map((i) => ({
      issueId: i.issueId, issueNumber: i.issueNumber, issueDate: formatDate(i.issueDate), recipient: i.recipientName, kind: RECIPIENT_LABELS[i.recipientType],
      items: formatQty(i.totalQuantity), returned: formatQty(i.returnedQuantity), status: i.status, by: i.issuedByName ?? '',
    })),
  );

  ngOnInit(): void {
    this.load();
  }

  protected apply(): void {
    if (this.from.value && this.to.value && this.to.value < this.from.value) {
      this.toast.danger("The 'to' date can't be before the 'from' date.");
      return;
    }
    this.page.set(1);
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.service
      .issues({ recipientType: this.type.value, status: this.status.value, search: this.search.value, from: this.from.value, to: this.to.value, page: this.page(), pageSize: this.pageSize() })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({ next: (p) => { this.list.set(p.items); this.total.set(p.totalCount); this.canManage.set(p.canManage); }, error: () => this.failed.set(true) });
  }

  protected onPage(p: number): void { this.page.set(p); this.load(); }
  protected onPageSize(s: number): void { this.pageSize.set(s); this.page.set(1); this.load(); }

  protected onIssued(issue: InvIssue): void {
    this.creating.set(false);
    this.page.set(1);
    this.load();
    this.openId.set(issue.issueId);   // straight to the new issue so a return can be recorded later from the same place
  }
}
