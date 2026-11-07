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

Measured on the development machine through the real endpoint, Term Spread, cover and rules pages switched on (3 pages per student), 12 real demo cards copied up to 100 (copies exist only in the scratch database), after the first request warmed the fonts:

| Cards in the file | Pages | Time | File size |
|---|---|---|---|
| 12 | 36 | 0.1 s | 0.17 MB |
| 40 | 120 | 0.3 s | 0.46 MB |
| 100 | 300 | 0.8 s | 1.10 MB |

- The API process peaked at about 343 MB working set during these runs (a process-wide figure, not per request). It did not move between 12 and 100 cards, so the cost of one request is small next to the process baseline. This is a single machine with one request at a time; treat it as an order of magnitude.
- The cap stays at **100 per request**: 100 cards took under a second, so the headroom in time is large and the cap is set by memory and by several people downloading at once, not by speed. At most two bulk renders run at the same time per server process; a third waits up to 15 seconds and then gets a 503 "try again in a minute".
- Database cost is fixed: a bulk request runs 4 commands (the access checks plus one query that reads every card of the version) for 12 cards and for 72 cards alike. This is asserted by the scratch smoke test. Logo and signature files are read once per request, not per student.
- Cards are drawn from the stored card models (never recomputed), so a bulk file shows exactly what was published, including each publication's own settings and signature.
