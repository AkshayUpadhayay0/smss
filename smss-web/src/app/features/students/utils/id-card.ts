import { saveBlob } from '../../homework/utils/homework-format';

/** The bulk response carries the PDF as base64 (so the report lists can travel with it): turn it back into a file and save it. */
export function savePdfBase64(base64: string, fileName: string): void {
  const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
  saveBlob(new Blob([bytes], { type: 'application/pdf' }), fileName);
}

/** "Student ID cards" header line for the report: how many cards, how many sheets. */
export function idCardSummary(cardCount: number, sheetCount: number): string {
  return `${cardCount} ${cardCount === 1 ? 'card' : 'cards'} on ${sheetCount} ${sheetCount === 1 ? 'sheet' : 'sheets'}`;
}
