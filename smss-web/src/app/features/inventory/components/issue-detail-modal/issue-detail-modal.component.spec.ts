import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { InvIssue, InvIssueStatus } from '../../models/inventory.model';
import { IssueDetailModalComponent } from './issue-detail-modal.component';

const API = `${environment.apiUrl}/api/InventoryIssue`;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

const issue = (status: InvIssueStatus, returned = 0): InvIssue => ({
  issueId: 7, issueNumber: 'ISS-2026-0007', recipientType: 'DEPARTMENT', recipientId: '3', recipientName: 'Science', issueDate: '2026-11-10', purpose: 'Lab week', issuedByName: 'Asha',
  status, totalQuantity: 5, returnedQuantity: returned, voidedByName: null, voidedAt: null, voidReason: null, createdAt: '2026-11-10T05:00:00Z',
  lines: [{
    issueLineId: 70, itemId: 1, itemCode: 'ITM0001', itemName: 'Beaker', unitCode: 'pcs', quantity: 5, returnedQuantity: returned, outstandingQuantity: 5 - returned,
    returns: returned ? [{ returnId: 1, quantity: returned, returnDate: '2026-11-12', condition: 'DAMAGED', remarks: 'cracked', receivedByName: 'Asha' }] : [],
  }],
});

describe('IssueDetailModalComponent', () => {
  let fixture: ComponentFixture<IssueDetailModalComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const buttonTexts = () => [...el().querySelectorAll('button')].map((b) => b.textContent?.trim() ?? '');

  function open(canManage: boolean, data: InvIssue) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(IssueDetailModalComponent);
    fixture.componentRef.setInput('issueId', 7);
    fixture.componentRef.setInput('canManage', canManage);
    fixture.detectChanges();
    http.expectOne(`${API}/7`).flush(env(data));
    fixture.detectChanges();
  }
  afterEach(() => http.verify());

  it('an Outstanding issue offers Record return and Void issue to a manager', () => {
    open(true, issue('Outstanding'));
    expect(text()).toContain('ISS-2026-0007');
    expect(text()).toContain('Science');
    expect(buttonTexts().join('|')).toContain('Record return');
    expect(buttonTexts().join('|')).toContain('Void issue');
  });

  it('once something is returned the issue can no longer be voided, but more can be returned', () => {
    open(true, issue('Partially Returned', 2));
    expect(buttonTexts().join('|')).not.toContain('Void issue');
    expect(buttonTexts().join('|')).toContain('Record return');
    expect(text()).toContain('damaged (not added to stock)');
  });

  it('a read-only viewer sees the issue and its returns but no action at all', () => {
    open(false, issue('Outstanding'));
    expect(text()).toContain('Beaker');
    expect(buttonTexts().join('|')).not.toMatch(/Record return|Void issue/);
    expect(el().querySelector('form[aria-label="Record a return"]')).toBeNull();
  });

  it('a voided or fully returned issue has no action either, even for a manager', () => {
    open(true, { ...issue('Voided'), voidReason: 'mistake', voidedByName: 'Asha', voidedAt: '2026-11-11T05:00:00Z' });
    expect(buttonTexts().join('|')).not.toMatch(/Record return|Void issue/);
    expect(text()).toContain('mistake');
  });

  it('recording a return sends the quantity and condition, rejecting more than is outstanding in the browser', () => {
    open(true, issue('Outstanding'));
    const qty = el().querySelector<HTMLInputElement>('.return-line input[type=number]')!;
    qty.value = '9';
    qty.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    [...el().querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.includes('Record return'))!.click();
    fixture.detectChanges();
    http.expectNone(`${API}/7/return`);
    expect(text()).toContain('Only 5 is outstanding');

    qty.value = '2';
    qty.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    [...el().querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.includes('Record return'))!.click();
    const req = http.expectOne(`${API}/7/return`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.lines).toEqual([{ issueLineId: 70, quantity: 2, condition: 'GOOD', remarks: null }]);
    req.flush(env(issue('Partially Returned', 2)));
    http.expectOne(`${API}/7`).flush(env(issue('Partially Returned', 2)));   // the modal reloads itself
  });

  it('Void issue asks for confirmation first; declining sends nothing', async () => {
    open(true, issue('Outstanding'));
    const confirm = vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(false);
    [...el().querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.includes('Void issue'))!.click();
    await Promise.resolve(); await Promise.resolve();
    fixture.detectChanges();
    expect(confirm).toHaveBeenCalledTimes(1);
    expect(confirm.mock.calls[0][0]).toMatchObject({ variant: 'danger' });
    expect(el().querySelector('app-void-reason-modal')).toBeNull();
    http.expectNone(`${API}/7/void`);
  });
});
