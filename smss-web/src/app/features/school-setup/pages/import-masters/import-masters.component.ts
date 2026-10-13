import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent } from '../../../../shared/components';
import {
  MASTER_IMPORT_GROUPS,
  MasterImportCatalog,
  MasterImportGroup,
  MasterImportGroupKey,
  MasterImportItem,
  MasterImportRequest,
  MasterImportResult,
} from '../../models/master-import.model';
import { MasterImportService } from '../../services/master-import.service';

type Selection = Record<MasterImportGroupKey, string[]>;

const emptySelection = (): Selection => ({ classes: [], subjects: [], designations: [], departments: [], admissionTypes: [], documentTypes: [] });

/**
 * One page to set up the school's masters in one go: tick what the school uses, press Import, and the classes (each
 * with Section A), subjects, designations, departments, admission types and document types are created. Items the
 * school already has are shown as "Already added" and are never touched.
 */
@Component({
  selector: 'app-import-masters',
  imports: [ButtonComponent, CardComponent, IconComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './import-masters.component.scss',
  templateUrl: './import-masters.component.html',
})
export class ImportMastersComponent implements OnInit {
  private readonly service = inject(MasterImportService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmDialogService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly groups = MASTER_IMPORT_GROUPS;
  protected readonly catalog = signal<MasterImportCatalog | null>(null);
  protected readonly selection = signal<Selection>(emptySelection());
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly importing = signal(false);
  protected readonly result = signal<MasterImportResult | null>(null);

  protected readonly breadcrumbs = [{ label: 'School Setup' }, { label: 'Import Masters' }];

  protected readonly totalSelected = computed(() => this.groups.reduce((n, g) => n + this.selection()[g.key].length, 0));

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .getCatalog()
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (c) => this.catalog.set(c),
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  protected items(group: MasterImportGroup): MasterImportItem[] {
    return this.catalog()?.[group.key] ?? [];
  }

  protected isSelected(group: MasterImportGroup, item: MasterImportItem): boolean {
    return this.selection()[group.key].includes(item.key);
  }

  protected toggle(group: MasterImportGroup, item: MasterImportItem): void {
    if (item.alreadyAdded) return;
    this.selection.update((s) => {
      const current = s[group.key];
      const next = current.includes(item.key) ? current.filter((k) => k !== item.key) : [...current, item.key];
      return { ...s, [group.key]: next };
    });
  }

  /** Items of the group that can still be picked. */
  private selectable(group: MasterImportGroup): MasterImportItem[] {
    return this.items(group).filter((i) => !i.alreadyAdded);
  }

  protected selectableCount(group: MasterImportGroup): number {
    return this.selectable(group).length;
  }

  protected allSelected(group: MasterImportGroup): boolean {
    const n = this.selectableCount(group);
    return n > 0 && this.selection()[group.key].length === n;
  }

  protected toggleAll(group: MasterImportGroup): void {
    const next = this.allSelected(group) ? [] : this.selectable(group).map((i) => i.key);
    this.selection.update((s) => ({ ...s, [group.key]: next }));
  }

  protected selectedIn(group: MasterImportGroup): number {
    return this.selection()[group.key].length;
  }

  protected clearAll(): void {
    this.selection.set(emptySelection());
  }

  protected async runImport(): Promise<void> {
    const total = this.totalSelected();
    if (total === 0 || this.importing()) return;

    const ok = await this.confirm.confirm({
      title: 'Import masters',
      message: `Add ${total} selected item${total === 1 ? '' : 's'} to your school's masters? Anything you already have stays as it is.`,
      confirmText: 'Import',
    });
    if (!ok) return;

    const s = this.selection();
    const request: MasterImportRequest = { ...s };
    this.importing.set(true);
    this.service
      .import(request)
      .pipe(finalize(() => this.importing.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => {
          this.result.set(r);
          this.clearAll();
          this.toast.success(r.totalCreated > 0 ? `Imported ${r.totalCreated} item${r.totalCreated === 1 ? '' : 's'}.` : 'Everything you selected was already set up.');
          this.load(); // refresh the "Already added" marks
        },
        error: () => undefined, // the server's reason is toasted by the interceptor; the selection is kept
      });
  }

  protected dismissResult(): void {
    this.result.set(null);
  }

  protected readonly resultRows = computed(() => {
    const r = this.result();
    if (!r) return [];
    return [
      { label: 'Classes', ...r.classes },
      { label: 'Sections', ...r.sections },
      { label: 'Subjects', ...r.subjects },
      { label: 'Employee designations', ...r.designations },
      { label: 'Employee departments', ...r.departments },
      { label: 'Admission types', ...r.admissionTypes },
      { label: 'Document types', ...r.documentTypes },
    ];
  });

  protected docMeta(item: MasterImportItem): string {
    if (!item.appliesTo) return '';
    const who = item.appliesTo === 'BOTH' ? 'Students & staff' : item.appliesTo === 'STUDENT' ? 'Students' : 'Staff';
    return item.isRequired ? `${who} · Required` : who;
  }
}
