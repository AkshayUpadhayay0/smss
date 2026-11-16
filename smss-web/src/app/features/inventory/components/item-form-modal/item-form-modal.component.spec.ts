import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { InvCategory, InvItem, InvUnit } from '../../models/inventory.model';
import { ItemFormModalComponent } from './item-form-modal.component';

const API = `${environment.apiUrl}/api/InventoryItem`;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const categories: InvCategory[] = [
  { categoryId: 1, categoryName: 'Stationery', categoryCode: 'STA', description: null, isActive: true, itemCount: 0 },
  { categoryId: 2, categoryName: 'Retired', categoryCode: 'OLD', description: null, isActive: false, itemCount: 0 },
];
const units: InvUnit[] = [{ unitId: 1, unitName: 'Pieces', unitCode: 'pcs' }];
const existing: InvItem = {
  itemId: 9, itemCode: 'ITM0009', barcode: null, itemName: 'Marker', description: null, categoryId: 1, categoryName: 'Stationery', unitId: 1, unitName: 'Pieces', unitCode: 'pcs', itemType: 'CONSUMABLE',
  purchasePrice: 12, sellingPrice: null, isSaleable: false, gstPercent: 0, minStockLevel: 3, currentQuantity: 40, stockValue: 480, expiryDate: null, hasPhoto: false, isActive: true, isLowStock: false, isExpired: false, isExpiringSoon: false,
};

describe('ItemFormModalComponent', () => {
  let fixture: ComponentFixture<ItemFormModalComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const cmp = () => fixture.componentInstance as unknown as { form: { controls: Record<string, unknown>; patchValue(v: object): void } };

  function open(item: InvItem | null) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ItemFormModalComponent);
    fixture.componentRef.setInput('item', item);
    fixture.componentRef.setInput('categories', categories);
    fixture.componentRef.setInput('units', units);
    fixture.detectChanges();
  }
  const submit = () => { [...el().querySelectorAll<HTMLButtonElement>('button')].find((b) => /^(Create|Update)$/.test(b.textContent?.trim() ?? ''))!.click(); fixture.detectChanges(); };
  afterEach(() => http.verify());

  it('has NO quantity field: stock can only change through the ledger', () => {
    open(null);
    expect(Object.keys(cmp().form.controls).some((k) => /quantity|stock$|opening/i.test(k.replace('minStockLevel', '')))).toBe(false);
    expect(text()).not.toMatch(/Current quantity|Opening stock|Quantity on hand/i);
    expect(text()).toContain('A new item starts at 0');
  });

  it('a new item has no photo section yet; an existing one does', () => {
    open(null);
    expect(el().querySelector('.file-pick')).toBeNull();
    TestBed.resetTestingModule();
    open(existing);
    expect(el().querySelector('.file-pick')).not.toBeNull();
    expect(text()).toContain('PNG, JPG or WEBP, up to 2 MB');
  });

  it('refuses to submit an empty form and sends nothing', () => {
    open(null);
    submit();
    http.expectNone(API);
    expect(text()).toContain('This field is required');
  });

  it('a saleable item needs a selling price', () => {
    open(null);
    cmp().form.patchValue({ itemName: 'Pen', categoryId: '1', unitId: '1', itemType: 'CONSUMABLE', isSaleable: true });
    fixture.detectChanges();
    submit();
    http.expectNone(API);
    expect(text()).toContain('A saleable item needs a selling price');
  });

  it('creates with the typed values, blank optional text as null, and no quantity in the body', () => {
    open(null);
    cmp().form.patchValue({ itemName: '  Pen  ', categoryId: '1', unitId: '1', itemType: 'CONSUMABLE', purchasePrice: '4.5', minStockLevel: '10', barcode: '   ' });
    fixture.detectChanges();
    submit();
    const req = http.expectOne(API);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      itemName: 'Pen', barcode: null, description: null, categoryId: 1, unitId: 1, itemType: 'CONSUMABLE', purchasePrice: 4.5, sellingPrice: null, isSaleable: false, gstPercent: 0, minStockLevel: 10, expiryDate: null,
    });
    req.flush(env({ ...existing, itemId: 10, itemName: 'Pen' }));
  });

  it('editing updates through PUT and never includes the quantity it already shows elsewhere', () => {
    open(existing);
    cmp().form.patchValue({ itemName: 'Marker (black)' });
    fixture.detectChanges();
    submit();
    const req = http.expectOne(`${API}/9`);
    expect(req.request.method).toBe('PUT');
    expect(Object.keys(req.request.body).some((k) => /quantity/i.test(k))).toBe(false);
    expect(req.request.body.itemName).toBe('Marker (black)');
    req.flush(env({ ...existing, itemName: 'Marker (black)' }));
  });

  it('an inactive category can not be chosen for a new item, but an item already in one keeps it', () => {
    open(null);
    expect(text()).not.toContain('Retired');
    TestBed.resetTestingModule();
    open({ ...existing, categoryId: 2, categoryName: 'Retired' });
    fixture.detectChanges();
    const opts = (fixture.componentInstance as unknown as { categoryOptions(): { label: string }[] }).categoryOptions().map((o) => o.label);
    expect(opts).toContain('Retired (inactive)');
  });
});
