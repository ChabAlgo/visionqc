# VisionQC 4.8.2 delivery

## Changes
- Explicit Output number-input dark/light colors.
- Deduplicated final Tool NG exports, selected date/Position exports and graph selection.
- History multi-Position filters and asynchronous streaming CSV export of the complete filtered, deduplicated result set.
- Analysis API capability 2; old agents cannot silently ignore Position filters for new exports.
- Web 4.8.2 / Agent 1.4.2.

## Validation
- SQLite fixture: 160 original observations / 80 day-Cell-Position records. AN(TOP), 2026-02-01, FoilDamage: 17 NG exported; Threshold 0.8: 10 exported. Date exports include 20 OK/NG Cells. Empty Position selection exports zero.
- Native HTTP test `selection-export-native.mjs`: private temporary DB and port 17932, 17/10 Tool NG exports, history page 10 vs total export 20, 7-row partitions, no existing user DB modified.
- Native fixture `history-integrity.ps1`: existing precision, rollback, recovery, approval, date/workspace tests plus new filter/export parity passed.
- Full browser regression: 82 passed; 10,000 classification files navigation median 28 ms, p95 43 ms in this synthetic local run. Additional remote export request test added separately.
- Added remote browser contract test and focused run: 10/10 passed; final right-side CSV layout/theme regression run: 7/7 passed. Total distinct browser tests: 83.
- Static regression: 144/144 passed. Installer audit: 17 embedded resources match sources, Core/8.0/8.2/Universal worker bundle checked; no user DB/images/Workspace/Cognex DLL included.
- Worker build: Launcher, Core, VPDL 8.0/8.2, Universal and worker archive succeeded.

## Limits
- No production-scale Cognex inference benchmark repeated for this export/UI-only change.
- Local installation must be verified separately from source push and package publication.
- Local silent installer execution was rejected by automatic execution review (`blocked by policy`) on 2026-09-21. No bypass attempted; previously read local status was Agent 1.3.36, idle. Source/package publication does not imply that this PC has been updated.
