import { csvCell } from './fee-report.model';

describe('csvCell', () => {
  it('quotes and escapes quotes', () => {
    expect(csvCell('A "B" C')).toBe('"A ""B"" C"');
    expect(csvCell(null)).toBe('""');
    expect(csvCell(2500)).toBe('"2500"');
  });

  it('defuses a leading = + - @ so a spreadsheet never runs a typed name as a formula', () => {
    for (const s of ['=1+1', '+SUM(A1)', '-2', '@x']) expect(csvCell(s)).toBe(`"'${s}"`);
  });
});
