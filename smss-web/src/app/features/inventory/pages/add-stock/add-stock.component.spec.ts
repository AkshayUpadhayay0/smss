import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { InvStockIn } from '../../models/inventory.model';
import { InventoryAddStockComponent } from './add-stock.component';

const API = `${environment.apiUrl}/api`;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

const stockIn = (id: number, over: Partial<InvStockIn> = {}): InvStockIn => ({
  stockInId: id, itemId: 1, itemCode: 'ITM0001', itemName: 'Chalk', unitCode: 'box', supplierId: null, supplierName: 'Sharma Stationers', quantity: 10, unitCost: 25, totalCost: 250,
  receivedDate: '2026-11-10', billNumber: 'B-17', expiryDate: null, remarks: null, receivedByName: 'Asha', status: 'Active', voidedByName: null, voidedAt: null, voidReason: null, createdAt: '2026-11-10T05:00:00Z', ...over,
});

describe('InventoryAddStockComponent: voiding a stock-in', () => {
  let fixture: ComponentFixture<InventoryAddStockComponent>;
  let http: HttpTestingController;
  let confirm: ReturnType<typeof vi.spyOn>;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const click = (label: string) => { [...el().querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim().includes(label))!.click(); fixture.detectChanges(); };
  const flushMicrotasks = async () => { await Promise.resolve(); await Promise.resolve(); fixture.detectChanges(); };

  function open(roles: string[], canManage: boolean, confirms: boolean) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'GHS261103002', roles }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    confirm = vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(confirms);
    fixture = TestBed.createComponent(InventoryAddStockComponent);
    fixture.detectChanges();
    http.expectOne((r) => r.url === `${API}/InventoryStockIn`).flush(env({ canManage, items: [stockIn(1), stockIn(2, { status: 'Voided', voidReason: 'dup', voidedByName: 'Asha', voidedAt: '2026-11-11T05:00:00Z' })], totalCount: 2, page: 1, pageSize: 10 }));
    fixture.detectChanges();
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('only an Active entry offers Void, and only to a manager', () => {
    open(['Store Keeper'], true, true);
    expect([...el().querySelectorAll('button')].filter((b) => b.textContent?.trim() === 'Void').length).toBe(1);
    TestBed.resetTestingModule();
    open(['Principal'], false, true);
    expect([...el().querySelectorAll('button')].some((b) => b.textContent?.trim() === 'Void')).toBe(false);   // the status badge may say "Voided": that is not an action
    expect([...el().querySelectorAll('button')].some((b) => /^Add Stock$/.test(b.textContent?.trim() ?? ''))).toBe(false);
  });

  it('declining the confirm dialog sends nothing and opens nothing', async () => {
    open(['Store Keeper'], true, false);
    click('Void');
    await flushMicrotasks();
    expect(confirm).toHaveBeenCalledTimes(1);
    expect(el().querySelector('app-void-reason-modal')).toBeNull();
    http.expectNone((r) => r.url.includes('/void'));
  });

  it('confirming asks for the reason next, and sends it with the void', async () => {
    open(['Store Keeper'], true, true);
    click('Void');
    await flushMicrotasks();
    expect(confirm.mock.calls[0][0]).toMatchObject({ variant: 'danger' });
    const modal = el().querySelector('app-void-reason-modal');
    expect(modal).not.toBeNull();

    // an empty reason is refused in the browser
    [...modal!.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.includes('Void stock-in'))!.click();
    fixture.detectChanges();
    http.expectNone((r) => r.url.includes('/void'));
    expect(text()).toContain('A reason is required');

    const area = modal!.querySelector('textarea')!;
    area.value = 'entered twice';
    area.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    [...modal!.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.includes('Void stock-in'))!.click();
    const req = http.expectOne(`${API}/InventoryStockIn/1/void`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ reason: 'entered twice' });
    req.flush(env(stockIn(1, { status: 'Voided' })));
    http.expectOne((r) => r.url === `${API}/InventoryStockIn`).flush(env({ canManage: true, items: [], totalCount: 0, page: 1, pageSize: 10 }));
    fixture.detectChanges();
    expect(el().querySelector('app-void-reason-modal')).toBeNull();
  });

  it('when the server refuses (stock already used) the reason form stays open so nothing typed is lost', async () => {
    open(['Store Keeper'], true, true);
    click('Void');
    await flushMicrotasks();
    const modal = el().querySelector('app-void-reason-modal')!;
    const area = modal.querySelector('textarea')!;
    area.value = 'wrong item';
    area.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    [...modal.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.includes('Void stock-in'))!.click();
    http.expectOne(`${API}/InventoryStockIn/1/void`).flush({ status: false, statusCode: 409, message: 'only 2 on hand', data: null }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(el().querySelector('app-void-reason-modal')).not.toBeNull();
    expect(el().querySelector('textarea')!.value).toBe('wrong item');
  });
});
