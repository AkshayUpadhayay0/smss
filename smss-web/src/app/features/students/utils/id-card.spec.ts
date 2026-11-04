import { idCardSummary, savePdfBase64 } from './id-card';

describe('id-card utils', () => {
  it('summarises cards and sheets with correct plurals', () => {
    expect(idCardSummary(1, 1)).toBe('1 card on 1 sheet');
    expect(idCardSummary(12, 2)).toBe('12 cards on 2 sheets');
  });

  it('turns the base64 PDF back into a PDF file and saves it under the given name', () => {
    let blob: Blob | undefined;
    URL.createObjectURL = vi.fn((b: Blob) => ((blob = b), 'blob:x'));
    URL.revokeObjectURL = vi.fn();
    let name = '';
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      name = this.download;
    });
    savePdfBase64(btoa('%PDF-1.7'), 'cards.pdf');
    expect(click).toHaveBeenCalledTimes(1);
    expect(name).toBe('cards.pdf');
    expect(blob?.type).toBe('application/pdf');
    expect(blob?.size).toBe(8);
    vi.restoreAllMocks();
  });
});
