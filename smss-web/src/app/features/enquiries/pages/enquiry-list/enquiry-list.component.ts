import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Subject, catchError, debounceTime, of, switchMap, tap } from 'rxjs';
import {
  ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, SelectComponent, SelectOption, TableColumn, TableComponent,
} from '../../../../shared/components';
import { EnquiryDetailDialogComponent } from '../../components/enquiry-detail-dialog/enquiry-detail-dialog.component';
import {
  ENQUIRY_STATUSES, EnquiryKind, EnquiryListItem, EnquiryStatus, STATUS_LABEL, STATUS_TONE, TYPE_LABEL,
} from '../../models/enquiry-admin.model';
import { EnquiryService } from '../../services/enquiry.service';

interface Row {
  enquiryId: number;
  received: string;
  type: string;
  name: string;
  school: string;
  email: string;
  phone: string;
  status: string;
  [key: string]: unknown;
}

type TypeTab = '' | EnquiryKind;

/** Enquiries from the public site (Super Admin): tabs for All / Demo / Contact, status, search and date filters, server-side paging. */
@Component({
  selector: 'app-enquiry-list',
  imports: [ReactiveFormsModule, PageHeaderComponent, CardComponent, ButtonComponent, IconComponent, InputComponent, SelectComponent, TableComponent, PaginationComponent, EnquiryDetailDialogComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './enquiry-list.component.scss',
  providers: [DatePipe],
  template: `
    <app-page-header title="Enquiries" subtitle="Contact messages and demo requests from the website" icon="inbox" [breadcrumbs]="[{ label: 'Enquiries' }]" />

    <app-card [noPadding]="true">
      <div class="tabs" role="tablist" aria-label="Enquiry type">
        @for (t of tabs; track t.value) {
          <button type="button" role="tab" [attr.aria-selected]="tab() === t.value" [class.active]="tab() === t.value" (click)="selectTab(t.value)">{{ t.label }}</button>
        }
      </div>

      <div class="filters">
        <div class="f-search"><app-input placeholder="Search name, email or school…" prefixIcon="search" [formControl]="searchControl" /></div>
        <div class="f-status"><app-select [formControl]="statusControl" placeholder="All statuses" [options]="statusOptions" /></div>
        <div class="f-date"><app-input label="From" type="date" [formControl]="fromControl" [errorText]="invalidRange() ? 'After the end date' : undefined" /></div>
        <div class="f-date"><app-input label="To" type="date" [formControl]="toControl" /></div>
        @if (hasFilters()) {
          <app-button variant="secondary" size="sm" (click)="clearFilters()">Clear filters</app-button>
        }
        <span class="count">{{ total() }} {{ total() === 1 ? 'enquiry' : 'enquiries' }}</span>
      </div>

      <app-table
        [columns]="columns"
        [rows]="rows()"
        [loading]="loading()"
        [hasActions]="true"
        [emptyMessage]="loadFailed() ? 'Could not load enquiries.' : hasFilters() ? 'No enquiries match these filters.' : 'No enquiries yet. They appear here when someone uses the website forms.'"
      >
        <ng-template #rowActions let-row>
          <app-button variant="secondary" size="sm" aria-label="View enquiry" title="View" (click)="open(row.enquiryId)"><app-icon name="eye" [size]="14" /> View</app-button>
        </ng-template>
      </app-table>

      @if (loadFailed()) {
        <div class="retry"><app-button variant="secondary" size="sm" (click)="reload()">Try again</app-button></div>
      }

      <app-pagination [currentPage]="page()" [totalItems]="total()" [pageSize]="pageSize()" (pageChange)="goToPage($event)" (pageSizeChange)="onPageSize($event)" />
    </app-card>

    @if (openId(); as id) {
      <app-enquiry-detail-dialog [enquiryId]="id" (closed)="openId.set(null)" (changed)="reload()" />
    }
  `,
})
export class EnquiryListComponent implements OnInit {
  private readonly service = inject(EnquiryService);
  private readonly route = inject(ActivatedRoute);
  private readonly datePipe = inject(DatePipe);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly tabs: { value: TypeTab; label: string }[] = [
    { value: '', label: 'All' },
    { value: 'DEMO', label: 'Demo requests' },
    { value: 'CONTACT', label: 'Contact messages' },
  ];
  protected readonly statusOptions: SelectOption[] = ENQUIRY_STATUSES.map((s) => ({ value: s, label: STATUS_LABEL[s] }));

  protected readonly columns: TableColumn<Row>[] = [
    { key: 'received', label: 'Received' },
    { key: 'type', label: 'Type' },
    { key: 'name', label: 'Name' },
    { key: 'school', label: 'School' },
    { key: 'email', label: 'Email' },
    { key: 'phone', label: 'Phone' },
    { key: 'status', label: 'Status', type: 'badge', tone: (v) => STATUS_TONE[this.statusFromLabel(String(v))] ?? 'gray' },
  ];

  protected readonly searchControl = new FormControl('', { nonNullable: true });
  protected readonly statusControl = new FormControl('', { nonNullable: true });
  protected readonly fromControl = new FormControl('', { nonNullable: true });
  protected readonly toControl = new FormControl('', { nonNullable: true });

  protected readonly tab = signal<TypeTab>('');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly total = signal(0);
  protected readonly items = signal<EnquiryListItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly openId = signal<number | null>(null);
  private readonly filterVersion = signal(0);

  protected readonly rows = computed<Row[]>(() =>
    this.items().map((i) => ({
      enquiryId: i.enquiryId,
      received: this.datePipe.transform(i.createdAt, 'dd MMM yyyy, h:mm a') ?? '',
      type: TYPE_LABEL[i.enquiryType],
      name: i.fullName,
      school: i.schoolName ?? '–',
      email: i.email,
      phone: i.phone ?? '–',
      status: STATUS_LABEL[i.status],
    })),
  );

  protected readonly invalidRange = computed(() => {
    this.filterVersion();
    const f = this.fromControl.value, t = this.toControl.value;
    return !!f && !!t && f > t;
  });
  protected readonly hasFilters = computed(() => {
    this.filterVersion();
    return !!(this.searchControl.value || this.statusControl.value || this.fromControl.value || this.toControl.value || this.tab());
  });

  private readonly load$ = new Subject<void>();

  constructor() {
    // Latest request wins: a slow answer to an old filter can never overwrite a newer one
    this.load$
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.loadFailed.set(false);
        }),
        switchMap(() =>
          this.service
            .list({
              type: this.tab(),
              status: this.statusControl.value as EnquiryStatus | '',
              from: this.fromControl.value,
              to: this.toControl.value,
              search: this.searchControl.value,
              page: this.page(),
              pageSize: this.pageSize(),
            })
            .pipe(
              catchError(() => {
                this.loadFailed.set(true);
                return of(null);
              }),
            ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((res) => {
        if (res) {
          this.items.set(res.items);
          this.total.set(res.total);
        } else {
          this.items.set([]);
          this.total.set(0);
        }
        this.loading.set(false);
      });
  }

  ngOnInit(): void {
    // Dashboard tiles link here with ?type=&status=
    const q = this.route.snapshot.queryParamMap;
    const type = q.get('type');
    const status = q.get('status');
    if (type === 'DEMO' || type === 'CONTACT') this.tab.set(type);
    if (status && (ENQUIRY_STATUSES as readonly string[]).includes(status)) this.statusControl.setValue(status);

    this.searchControl.valueChanges.pipe(debounceTime(300), takeUntilDestroyed(this.destroyRef)).subscribe(() => this.filtersChanged());
    for (const c of [this.statusControl, this.fromControl, this.toControl]) {
      c.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.filtersChanged());
    }
    this.load$.next();
  }

  protected selectTab(value: TypeTab): void {
    if (this.tab() === value) return;
    this.tab.set(value);
    this.filtersChanged();
  }

  protected clearFilters(): void {
    this.searchControl.setValue('', { emitEvent: false });
    this.statusControl.setValue('', { emitEvent: false });
    this.fromControl.setValue('', { emitEvent: false });
    this.toControl.setValue('', { emitEvent: false });
    this.tab.set('');
    this.filtersChanged();
  }

  protected goToPage(p: number): void {
    this.page.set(p);
    this.load$.next();
  }

  protected onPageSize(size: number): void {
    this.pageSize.set(size);
    this.page.set(1);
    this.load$.next();
  }

  protected open(id: number): void {
    this.openId.set(id);
  }

  protected reload(): void {
    this.load$.next();
  }

  private filtersChanged(): void {
    this.filterVersion.update((v) => v + 1);
    this.page.set(1);
    if (this.invalidRange()) return; // the date field explains; no request until the range makes sense
    this.load$.next();
  }

  private statusFromLabel(label: string): EnquiryStatus {
    return (ENQUIRY_STATUSES.find((s) => STATUS_LABEL[s] === label) ?? 'NEW') as EnquiryStatus;
  }
}
