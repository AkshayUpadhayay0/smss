import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { InvItem } from '../../models/inventory.model';
import { InventoryItemsComponent } from './items.component';

const API = `${environment.apiUrl}/api`;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

const item = (id: number, over: Partial<InvItem> = {}): InvItem => ({
  itemId: id, itemCode: `ITM000${id}`, barcode: null, itemName: `Item ${id}`, description: null, categoryId: 1, categoryName: 'Stationery', unitId: 1, unitName: 'Pieces', unitCode: 'pcs',
  itemType: 'CONSUMABLE', purchasePrice: 10, sellingPrice: null, isSaleable: false, gstPercent: 0, minStockLevel: 5, currentQuantity: 20, stockValue: 200, expiryDate: null,
  hasPhoto: false, isActive: true, isLowStock: false, isExpired: false, isExpiringSoon: false, ...over,
});

describe('InventoryItemsComponent', () => {
  let fixture: ComponentFixture<InventoryItemsComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  // the aria-label sits on the <app-button> host, the text on its inner <button>: read both
  const buttons = () => [...el().querySelectorAll('app-button')].map((b) => `${b.textContent?.trim() ?? ''}|${b.getAttribute('aria-label') ?? ''}`);

  function open(roles: string[], canManage: boolean, items: InvItem[]) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'GHS261103002', roles }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(InventoryItemsComponent);
    fixture.detectChanges();
    http.expectOne((r) => r.url === `${API}/InventoryCategory`).flush(env({ canManage, items: [{ categoryId: 1, categoryName: 'Stationery', categoryCode: 'STA', description: null, isActive: true, itemCount: items.length }] }));
    http.expectOne(`${API}/InventoryLookup/units`).flush(env([{ unitId: 1, unitName: 'Pieces', unitCode: 'pcs' }]));
    http.expectOne((r) => r.url === `${API}/InventoryItem`).flush(env({ canManage, items, totalCount: items.length, page: 1, pageSize: 10 }));
    fixture.detectChanges();
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('a Store Keeper / School Admin gets Add Item and Edit / Deactivate on each row', () => {
    open(['Store Keeper'], true, [item(1)]);
    const b = buttons().join(' ');
    expect(b).toContain('Add Item');
    expect(b).toContain('|Edit');
    expect(b).toContain('Deactivate');
    expect(b).toContain('|Stock history');
    expect(text()).toContain('Item 1');
    expect(text()).toContain('20 pcs');
  });

  it('the Principal sees the same items with NO action button: only the read-only stock history', () => {
    open(['Principal'], false, [item(1), item(2, { isActive: false })]);
    const b = buttons().join(' ');
    expect(b).not.toMatch(/Add Item|Edit|Deactivate|Activate|Add Category/);
    expect(b).toContain('|Stock history');
    expect(text()).toContain('Item 1');
    expect(text()).toContain('Item 2');
  });

  it('shows a loading-free empty state when nothing matches', () => {
    open(['Store Keeper'], true, []);
    expect(text()).toContain('No items match');
  });

  it('shows the stock label of each item (Low stock, Out of stock, Expired)', () => {
    open(['Store Keeper'], true, [item(1, { isLowStock: true, currentQuantity: 2 }), item(2, { currentQuantity: 0 }), item(3, { isExpired: true })]);
    expect(text()).toContain('Low stock');
    expect(text()).toContain('Out of stock');
    expect(text()).toContain('Expired');
  });

  it('an item photo is fetched through the authenticated API path (never a public URL)', () => {
    const created = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:test/1');
    open(['Store Keeper'], true, [item(1, { hasPhoto: true })]);
    const photo = http.expectOne(`${API}/InventoryItem/1/photo?v=0`);
    expect(photo.request.method).toBe('GET');
    photo.flush(new Blob(['x'], { type: 'image/png' }));
    fixture.detectChanges();
    expect(created).toHaveBeenCalled();
    expect([...el().querySelectorAll('img')].every((i) => i.getAttribute('src')?.startsWith('blob:'))).toBe(true);
    expect(el().innerHTML).not.toContain('/uploads');
    created.mockRestore();
  });

  it('switching to Categories shows the category list, read-only for the Principal', () => {
    open(['Principal'], false, [item(1)]);
    [...el().querySelectorAll<HTMLButtonElement>('.segmented button')].find((b) => b.textContent?.includes('Categories'))!.click();
    fixture.detectChanges();
    http.expectOne((r) => r.url === `${API}/InventoryCategory`).flush(env({ canManage: false, items: [{ categoryId: 1, categoryName: 'Stationery', categoryCode: 'STA', description: null, isActive: true, itemCount: 1 }] }));
    fixture.detectChanges();
    expect(text()).toContain('Stationery');
    expect(buttons().join(' ')).not.toMatch(/Add Category|Edit|Deactivate/);
  });
});
