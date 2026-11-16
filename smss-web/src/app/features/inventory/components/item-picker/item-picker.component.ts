import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { debounceTime, distinctUntilChanged, finalize } from 'rxjs';
import { InputComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { InvItem, formatQty } from '../../models/inventory.model';
import { InventoryService } from '../../services/inventory.service';

/**
 * Pick one ACTIVE item of the school: type to search by name, code or barcode (the server answers at most 20), then choose from the list.
 * The chosen item stays in the list even when a later search no longer matches it.
 */
@Component({
  selector: 'app-item-picker',
  imports: [ReactiveFormsModule, InputComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './item-picker.component.scss',
  template: `
    <div class="picker">
      <app-input [label]="label()" prefixIcon="search" placeholder="Search item by name, code or barcode…" [formControl]="search" />
      <app-select
        [label]="loading() ? 'Item (searching…)' : 'Item'"
        [required]="required()"
        [options]="options()"
        [placeholder]="noMatch() ? 'No active item matches' : 'Choose an item'"
        [formControl]="choice"
        [errorText]="errorText()"
      />
    </div>
  `,
})
export class ItemPickerComponent implements OnInit {
  private readonly service = inject(InventoryService);
  private readonly destroyRef = inject(DestroyRef);

  readonly label = input('Find item');
  readonly required = input(true);
  readonly errorText = input<string>();
  /** Item ids that can not be chosen again (already on another line of the same form). */
  readonly disabledIds = input<readonly number[]>([]);
  readonly picked = output<InvItem | null>();

  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly choice = new FormControl('', { nonNullable: true });
  protected readonly results = signal<InvItem[]>([]);
  protected readonly selected = signal<InvItem | null>(null);
  protected readonly loading = signal(false);
  protected readonly noMatch = computed(() => !this.loading() && this.results().length === 0);

  protected readonly options = computed<SelectOption[]>(() => {
    const list = [...this.results()];
    const sel = this.selected();
    if (sel && !list.some((i) => i.itemId === sel.itemId)) list.unshift(sel);
    return list.map((i) => ({ value: String(i.itemId), label: `${i.itemCode} · ${i.itemName} (${formatQty(i.currentQuantity)} ${i.unitCode})`, disabled: this.disabledIds().includes(i.itemId) && i.itemId !== sel?.itemId }));
  });

  ngOnInit(): void {
    this.load('');
    this.search.valueChanges.pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef)).subscribe((q) => this.load(q));
    this.choice.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => {
      const item = this.results().find((i) => String(i.itemId) === v) ?? (this.selected()?.itemId === Number(v) ? this.selected() : null);
      this.selected.set(item);
      this.picked.emit(item);
    });
  }

  private load(q: string): void {
    this.loading.set(true);
    this.service
      .items({ search: q, status: 'active', page: 1, pageSize: 20 })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({ next: (p) => this.results.set(p.items), error: () => this.results.set([]) });
  }
}
