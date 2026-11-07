# Report card rendering: measured cost and the bulk cap

Written 2026-11-07 for Stage 5a. The numbers below are measurements, not estimates. Section 3 is filled in again when the real designs are measured through the API.

## 1. Synthetic spike (before any design was built)

**What was rendered.** A throwaway console program (not part of the repository) that uses the application's own `PdfBootstrap` (QuestPDF Community, embedded Hind and PT Serif fonts) and draws, for each student, a **two-page A4 portrait** document:

- page 1: header, student block (Hindi and Latin names, father, mother, roll, admission number), a ruled table of **14 subjects x 6 exams** with a total, a grade and a per-subject remark column, co-scholastic and attendance lines, a remark in Hindi or English;
- page 2: the year summary table (14 subjects x 6 exams + year total + grade), the result line and a three-signature row.

Names and remarks were a mix of Devanagari and Latin text. Release build, .NET 10, one warm-up render that is not counted. Each size (40, 60 students) was run in its own process so the peak memory figure is clean.

**Machine.** Intel Core i5-8400 (6 logical processors), 24 GB RAM, Windows 11. The baseline process (runtime, fonts loaded, nothing rendered) used 54 MB.

| | 40 students | 60 students |
|---|---|---|
| One bulk PDF, all students (pages) | 80 | 120 |
| Time for the whole bulk PDF | **0.68 s** | **0.89 s** |
| Time per student inside the bulk PDF | 17 ms | 15 ms |
| File size of the bulk PDF | 0.6 MB (14 KB/student) | 0.8 MB (14 KB/student) |
| Peak memory above the baseline | **+48 MB** | **+72 MB** |
| One student on its own: median / p95 / max | 15 / 20 / 20 ms | 13 / 15 / 17 ms |
| One student on its own: file size | 45 KB | 45 KB |
| 40 or 60 single PDFs back to back | 0.63 s | 0.80 s |

Reading the numbers: time grows linearly (about 15 to 17 ms per student) and memory grows roughly linearly too (about 1.2 MB per student, because the whole document is built in memory before it is written). A single-student PDF costs about the same as one student inside a bulk PDF plus a small fixed cost (45 KB because the fonts are embedded each time, 14 KB per student once they are shared in a bulk file).

## 2. The bulk cap

**Cap: 100 students per request** (`ReportCardBulk.MaxStudentsPerRequest`).

- A school section is almost always 20 to 60 students (the largest in the demo data is 12; the section strength field defaults to 40). 60 is the largest size measured.
- At 100 students the spike predicts about 1.5 s of rendering and about +120 MB of memory for one request.
- **Headroom left: 1.7x over the largest measured section (60), 2.5x over the usual 40.** The cap is deliberately well inside what one request can do, because the real designs draw more than the spike (a logo and a signature image on every page, cover and rules pages, shaded tables, three terms), and because several people may press "Download all" at the same time on a small server.
- Over the cap the API answers 409 with a plain message and offers to split by roll range (for example "rolls 1 to 100", "101 to 150"); nothing is truncated silently.

## 3. Real designs through the API

(Filled in by Step 7: the three real designs, cover and rules pages on, with a logo and a signature image, measured through `POST /api/ReportCard/{publicationId}/bulk`.)
