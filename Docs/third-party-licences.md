# Third-party packages: licences (Excel / document generation)

Checked from each package's own .nuspec (the `license` expression) on 2026-11-06, via `dotnet list package --include-transitive`.

| Package | Version | Licence | Why it is here |
|---|---|---|---|
| ClosedXML | 0.105.1 | MIT | Read and write .xlsx (student import template, preview, error report) |
| ClosedXML.Parser | 2.0.0 | MIT | formula parser used by ClosedXML (formulas are never evaluated by our code) |
| DocumentFormat.OpenXml | 3.1.1 | MIT | OpenXML SDK, used by ClosedXML |
| DocumentFormat.OpenXml.Framework | 3.1.1 | MIT | same |
| ExcelNumberFormat | 1.1.0 | MIT | number-format parsing, used by ClosedXML |
| RBush.Signed | 4.0.0 | MIT | spatial index, used by ClosedXML |
| System.IO.Packaging | 8.0.1 | MIT | OPC package support |
| SixLabors.Fonts | 1.0.0 | **Apache-2.0** | font metrics for column auto-fit, used by ClosedXML |

**SixLabors.Fonts is pinned below 2.0** (`[1.0.0,2.0.0)` in smss-api-service-layer.csproj). Version 2.x moves to the Six Labors Split License,
which is not a plain open-source licence. ClosedXML 0.105.1 already resolves 1.0.0, so no workaround was needed.

EPPlus is **not** used (its v5+ licence is commercial/PolyForm Noncommercial).

Other document package already in the project: QuestPDF (licence recorded when it was added).
