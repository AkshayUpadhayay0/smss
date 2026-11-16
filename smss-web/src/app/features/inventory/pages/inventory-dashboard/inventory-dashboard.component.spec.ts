import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { InvDashboard } from '../../models/inventory.model';
import { InventoryDashboardComponent } from './inventory-dashboard.component';

const URL_ = `${environment.apiUrl}/api/InventoryDashboard/summary`;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

const full = (over: Partial<InvDashboard> = {}): InvDashboard => ({
  canManage: true, expiringSoonDays: 30, totalActiveItems: 42, totalStockValue: 12500.5,
  lowStockCount: 2, lowStock: [{ itemId: 1, itemCode: 'ITM0001', itemName: 'Chalk', unitCode: 'box', currentQuantity: 1, minStockLevel: 5, expiryDate: null }],
  expiringSoonCount: 1, expiringSoon: [{ itemId: 2, itemCode: 'ITM0002', itemName: 'Glue', unitCode: 'pcs', currentQuantity: 4, minStockLevel: 0, expiryDate: '2026-12-01' }],
  expiredCount: 0, expired: [], issuedNotReturnedCount: 3,
  recentMovements: [{ movementId: 1, itemId: 1, itemCode: 'ITM0001', itemName: 'Chalk', unitCode: 'box', movementType: 'ISSUE', quantityChange: -2, quantityAfter: 1, reasonCode: null, referenceType: null, referenceId: null, unitCost: null, remarks: null, createdByName: 'Asha', createdAt: '2026-11-16T05:00:00Z' }],
  stockInsLast30Days: 6, issuesLast30Days: 9, unavailableParts: [], ...over,
});

describe('InventoryDashboardComponent', () => {
  let fixture: ComponentFixture<InventoryDashboardComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');

  function open(roles: string[], data: InvDashboard | 'fail') {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'GHS261103002', roles }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(InventoryDashboardComponent);
    fixture.detectChanges();
    const req = http.expectOne(URL_);
    if (data === 'fail') req.flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    else req.flush(env(data));
    fixture.detectChanges();
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('shows the figures, the lists and the recent movements from ONE request', () => {
    open(['Store Keeper'], full());
    for (const t of ['42', '₹12,500.50', 'Chalk', 'Glue', 'Issued', '-2 box']) expect(text()).toContain(t);
    expect(text()).toContain('Expiring in 30 days');
    expect(text()).toContain('No item with stock is past its expiry date.');   // an empty list has an empty state
    expect(el().textContent).not.toContain('Unavailable');
  });

  it('a part that could not be loaded reads "Unavailable", never a false 0', () => {
    open(['School Admin'], full({ totalStockValue: null, lowStockCount: null, lowStock: null, issuedNotReturnedCount: null, unavailableParts: ['totalStockValue', 'lowStock', 'issuedNotReturned'] }));
    expect(text()).toContain('Unavailable');
    expect(el().querySelectorAll('.value.unavailable').length).toBe(3);
    expect(text()).toContain('Some figures could not be loaded');
    // the parts that did load are still there, and 42 is not replaced by anything
    expect(text()).toContain('42');
  });

  it('the Principal gets the numbers but no action link (canManage is false)', () => {
    open(['Principal'], full({ canManage: false }));
    expect(text()).not.toMatch(/Add stock|Issue items/);
    expect(text()).toContain('42');
  });

  it('a manager gets the two quick actions', () => {
    open(['Store Keeper'], full());
    expect(text()).toContain('Add stock');
    expect(text()).toContain('Issue items');
  });

  it('a failed request shows an error state with a retry, not zeros', () => {
    open(['Store Keeper'], 'fail');
    expect(text()).toContain('Could not load the inventory summary.');
    expect(text()).toContain('Try again');
    expect(el().querySelector('.stat')).toBeNull();
  });
});
