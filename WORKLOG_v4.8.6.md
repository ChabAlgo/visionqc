# VisionQC 4.8.6 / Agent 1.4.6

## Scope and counting rule
- Main dashboard daily inspection chart/KPIs and report daily chart now count date + Cell ID once across Positions. A Cell is NG when any selected Position is final NG after Threshold filtering. Different dates are separate inspections; missing-date rows remain excluded from the date chart.
- Agent default all-Position path reuses existing incremental cell_stats counters. Position subsets aggregate MAX(ng) per day/Cell in SQLite. No raw browser dataset added, no stored observations changed.
- Browser fallback merges the same Cell identity; legacy live date buckets keep incremental per-Cell NG reference counts rather than rescanning prior results.
- Position cards, Tool NG CSVs, raw export and history record pagination remain unchanged. CSV detail rows may outnumber Cells because Position details are retained.
- Agent dashboard reports dailyUnit=cell. Older Agents cannot silently supply Position totals under Cell labels; main chart shows an upgrade notice instead. PDF excludes incompatible older-Agent daily data.

## Verification
- Browser: two dates, two Positions, repeated rows, overlapping Cell IDs. Overall 40 Cells instead of 80 Position records; selected date 20; AN Threshold .80 leaves 10 NG but both Positions still have 17 NG Cells; AN-only selection gives 10/20 (50%); empty selection gives zero. PDF daily chart shows 85%, not Position-weighted 67.5%.
- Native HTTP same cases: 40 overall Cells / 34 NG, daily 20/17; changing CA Threshold to .95 with AN .80 leaves 10 NG; subset and empty selection pass. Export/reimport and Tool CSV 17 -> 10 checks pass.
- Native test data: C:/Users/HP/AppData/Local/Temp/VisionQC-482-selection-mnjvbi.
- Isolated SQLite history integrity passed; existing data preserved.
- Static 144/144 passed. Full browser run initially had 87 passes and one unrelated asynchronous preset import assertion reading storage before import completion. Changed that test to wait for persisted state, rather than an already-equal input value; recheck result recorded below.
- Worker builds and artifact audit passed (17 resources; no user data/Cognex DLL).
- Targeted browser recheck: 20/20 passed, including new old-Agent compatibility coverage and PDF Cell-rate assertion (89 distinct browser scenarios total).
- No production-size multi-day GPU or long-duration memory-leak guarantee is made by these fixtures.
- Prior automatic installer execution was rejected by automatic review with blocked by policy (no further reason). No installation bypass; local update not claimed.
