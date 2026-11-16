import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import { InventoryService, cleanParams } from './inventory.service';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

describe('InventoryService', () => {
  let http: HttpTestingController;
  let service: InventoryService;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(InventoryService);
  });
  afterEach(() => http.verify());

  it('cleanParams drops blanks, nulls and false flags', () => {
    expect(cleanParams({ a: 1, b: '', c: null, d: undefined, e: false, f: true, g: 'x' })).toEqual({ a: 1, f: true, g: 'x' });
  });

  it('lists items with only the filters that are set, and unwraps the envelope', () => {
    let got: unknown;
    service.items({ search: ' pen ', categoryId: 3, status: 'active', lowStock: true, page: 2, pageSize: 10 }).subscribe((p) => (got = p));
    const req = http.expectOne((r) => r.url === `${API}/api/InventoryItem`);
    expect(req.request.method).toBe('GET');
    expect(Object.fromEntries(req.request.params.keys().map((k) => [k, req.request.params.get(k)]))).toEqual({ search: 'pen', categoryId: '3', status: 'active', lowStock: 'true', page: '2', pageSize: '10' });
    req.flush(env({ canManage: true, items: [], totalCount: 0, page: 2, pageSize: 10 }));
    expect(got).toEqual({ canManage: true, items: [], totalCount: 0, page: 2, pageSize: 10 });
  });

  it('every non-idempotent action is a POST (never PATCH) on the documented path', () => {
    const calls: [() => void, string][] = [
      [() => service.toggleItem(5).subscribe(), 'api/InventoryItem/5/toggle-status'],
      [() => service.toggleCategory(5).subscribe(), 'api/InventoryCategory/5/toggle-status'],
      [() => service.toggleSupplier(5).subscribe(), 'api/InventorySupplier/5/toggle-status'],
      [() => service.removePhoto(5).subscribe(), 'api/InventoryItem/5/photo/remove'],
      [() => service.voidStockIn(5, 'why').subscribe(), 'api/InventoryStockIn/5/void'],
      [() => service.voidIssue(5, 'why').subscribe(), 'api/InventoryIssue/5/void'],
      [() => service.returnIssue(5, { returnDate: '2026-11-16', lines: [] }).subscribe(), 'api/InventoryIssue/5/return'],
    ];
    for (const [call, path] of calls) {
      call();
      const req = http.expectOne(`${API}/${path}`);
      expect(req.request.method).toBe('POST');
      req.flush(env({}));
    }
  });

  it('a void sends its reason, and a stock-in carries no school id (the server reads it from the token)', () => {
    service.voidStockIn(7, 'entered twice').subscribe();
    expect(http.expectOne(`${API}/api/InventoryStockIn/7/void`).request.body).toEqual({ reason: 'entered twice' });
    service.createStockIn({ itemId: 1, supplierId: null, quantity: 2, unitCost: 3, receivedDate: '2026-11-16', billNumber: null, expiryDate: null, remarks: null }).subscribe();
    const body = http.expectOne(`${API}/api/InventoryStockIn`).request.body as Record<string, unknown>;
    expect(Object.keys(body)).not.toContain('schoolId');
  });

  it('saving an item sends no quantity, on create or update', () => {
    const body = { itemName: 'Pen', barcode: null, description: null, categoryId: 1, unitId: 1, itemType: 'CONSUMABLE', purchasePrice: 5, sellingPrice: null, isSaleable: false, gstPercent: 0, minStockLevel: 0, expiryDate: null };
    service.saveItem(null, body).subscribe();
    const create = http.expectOne(`${API}/api/InventoryItem`);
    expect(create.request.method).toBe('POST');
    expect(Object.keys(create.request.body).some((k) => /quantity/i.test(k))).toBe(false);
    service.saveItem(9, body).subscribe();
    const update = http.expectOne(`${API}/api/InventoryItem/9`);
    expect(update.request.method).toBe('PUT');
    expect(Object.keys(update.request.body).some((k) => /quantity/i.test(k))).toBe(false);
  });

  it('a photo is addressed by the API path with a version that changes when it is replaced', () => {
    const before = service.photoPath(4);
    expect(before.startsWith('/api/InventoryItem/4/photo?v=')).toBe(true);
    service.bumpPhoto(4);
    expect(service.photoPath(4)).not.toBe(before);
    expect(service.photoPath(5)).toBe('/api/InventoryItem/5/photo?v=0');
  });
});
