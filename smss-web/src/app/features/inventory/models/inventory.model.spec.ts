import { formatQty, issueTone, moneyError, movementLabel, quantityError, signedAdjustment, stockLabel, stockTone } from './inventory.model';

const item = (over: Partial<Parameters<typeof stockLabel>[0]> = {}) => ({ currentQuantity: 10, isLowStock: false, isExpired: false, isExpiringSoon: false, ...over });

describe('inventory model helpers', () => {
  it('formats quantities without trailing zeros', () => {
    expect(formatQty(5)).toBe('5');
    expect(formatQty(2.5)).toBe('2.5');
    expect(formatQty(0.125)).toBe('0.125');
    expect(formatQty(null)).toBe('—');
  });

  it('labels stock worst-first: Expired, Out of stock, Low stock, Expiring soon, In stock', () => {
    expect(stockLabel(item({ isExpired: true, isLowStock: true }))).toBe('Expired');
    expect(stockLabel(item({ currentQuantity: 0, isLowStock: true }))).toBe('Out of stock');
    expect(stockLabel(item({ isLowStock: true, isExpiringSoon: true }))).toBe('Low stock');
    expect(stockLabel(item({ isExpiringSoon: true }))).toBe('Expiring soon');
    expect(stockLabel(item())).toBe('In stock');
    expect([stockTone('In stock'), stockTone('Low stock'), stockTone('Expired')]).toEqual(['green', 'yellow', 'red']);
  });

  it('colours issue statuses and names movements', () => {
    expect([issueTone('Returned'), issueTone('Partially Returned'), issueTone('Outstanding'), issueTone('Voided')]).toEqual(['green', 'blue', 'yellow', 'gray-strike']);
    expect(movementLabel('STOCK_IN_VOID')).toBe('Stock-in voided');
    expect(movementLabel('SOMETHING_NEW')).toBe('SOMETHING_NEW');
  });

  it('turns a typed positive amount into the signed change the server expects', () => {
    expect(signedAdjustment('DAMAGE', 3, 'IN')).toBe(-3);
    expect(signedAdjustment('LOSS', 3, 'IN')).toBe(-3);
    expect(signedAdjustment('EXPIRED', 3, 'IN')).toBe(-3);
    expect(signedAdjustment('OPENING_BALANCE', 3, 'OUT')).toBe(3);
    expect(signedAdjustment('CORRECTION', 3, 'IN')).toBe(3);
    expect(signedAdjustment('CORRECTION', 3, 'OUT')).toBe(-3);
  });

  it('checks quantities (positive, at most 3 decimals) and money (not negative, at most 2 decimals)', () => {
    expect(quantityError('')).toBeNull();
    expect(quantityError('2.5')).toBeNull();
    expect(quantityError('0')).toBe('Must be greater than 0');
    expect(quantityError('-1')).toBe('Must be greater than 0');
    expect(quantityError('1.2345')).toBe('Use at most 3 decimal places');
    expect(quantityError('abc')).toBe('Enter a number');
    expect(moneyError('0')).toBeNull();
    expect(moneyError('12.34')).toBeNull();
    expect(moneyError('-1')).toBe('Can not be negative');
    expect(moneyError('1.234')).toBe('Use at most 2 decimal places');
  });
});
