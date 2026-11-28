# Outstanding from earlier years (no migration)

**Decision:** nothing is carried forward. No due row is moved or created; an old year's dues stay in their own year. Wherever a student's dues are shown, a derived amount "Outstanding from earlier years" is shown beside the year's own figures, per earlier year (years that START before the year shown).

| Where | What changed |
|---|---|
| `GET api/StudentFee/{id}/fee-dues` | `earlierYears[]`, `outstandingEarlierYears`, `earlierDues[]` (the unpaid installments of earlier years, oldest first) |
| Fees tab, payment dialog | Summary tile per year. The dialog lists earlier-year installments first, pre-ticked, labelled with the year; untick to pay only this year. Payments were already student-wide (auto mode locks every outstanding due, oldest first) |
| Student My fees, parent Fees (web, mobile) | "Outstanding from earlier years" card with the per-year amounts and installments |
| Parent children list and fee tile (`GET api/Family/children`) | `feeEarlierYears` per child, `totalEarlierYears`; the tile is "Paid" only when nothing is due now AND nothing is owed from earlier years. Only the guardian's own linked children are read |
| Class fee report | Separate "Earlier years" column and total (never added to the year's figures). A student who left or was transferred out stays on the report, flagged "Left", while anything is unpaid; once paid they drop off |
| Fee reminder (push) | An installment unpaid from an earlier year counts toward the overdue count. The text is unchanged and has no amounts |
| Late fee | Unchanged: it is per installment from its own due date, so an earlier-year installment paid late is charged like any other |

Derived by `IEarlierYearDuesRepository` (read-only SQL over `tb_student_fee_dues` joined to the item's year). Every query carries the school id. Not changed: the fee dashboard, section fee status and the student list keep their single-year figures.
