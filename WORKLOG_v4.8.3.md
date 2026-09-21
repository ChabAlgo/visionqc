# VisionQC 4.8.3 / Agent 1.4.3

## Scope
- Manifest generation is opt-in. Slot CSVs, dashboard exports, history exports and final merged CSVs do not generate sidecars.
- Green output retains a temporary manifest for parent Position workers. Successful single execution or completed parent merge removes only the specific input manifest, after all CSV files are published. Failed merge preserves retry inventory.
- No directory-wide automatic cleanup; no CSV/image/DB/Workspace removal.

## Additional integrity finding
- New export/reimport chain exposed a prior gap: blank Time in aggregate CSV caused Agent import to discard a valid Date, collapsing records across dates (80 -> 40).
- Import now preserves date-only timestamps, and Date/Time export retains that date without inventing a time.
- Native HTTP retest passed: 2 input CSVs, 80 deduplicated date/Cell/Position rows, 8 date-split exports, 8-file reimport preserves 80 rows, AN(TOP) 2026-02-01 has 20 rows/10 NG and Tool export contains 10 rows.

## Existing folder cleanup
- User-requested output root: C:/Temp/AUtempR/0.Download/000/0.
- Local Agent was 1.4.2 and idle. Validated all listed CSV members and exact DataRows against each manifest before deleting six .csv.parts.txt files.
- CSV and image files unchanged.

## Verification
- DataIntegrity fixture covers 37 date-partitioned records plus one merged record, 38 retained after successful cleanup; failed merge and single-source cleanup; empty export no manifest; quoted newlines, score precision, strict date parsing.
- History integrity fixtures and native HTTP selection/export tests run against isolated temporary DBs.
- Browser test added for multiple selection -> all paths submitted -> bounded Agent dashboard, graph and CSV controls.
- Long-duration production-size memory-leak testing is outside this patch validation.
- Static regression 144/144 passed. Full browser run: 83 passed, one legacy-only file-picker test encountered the real installed Agent; isolated that fallback test from local Agent access and reran its four-test file, 4/4 passed. Distinct browser scenarios: 84.
- Real Cognex runtime: 4 Position x 3 images x 3 runs = 36 inspections, plus one single-Position run of 3 images. Both paths finished with no .parts.txt, retained CSV rows, successfully reimported the partitions, saved history once and restored analysis after restart.
- Native reports: C:/Users/HP/AppData/Local/Temp/VisionQC-481-candidate-e7nmc6/report.json and C:/Users/HP/AppData/Local/Temp/VisionQC-481-candidate-YIW1MD/report.json.
- Installer audit passed for 17 embedded resources and Core/8.0/8.2/Universal workers; no user data or Cognex DLL included.
- Local Agent 1.4.3 silent installation was rejected by automatic execution review (`blocked by policy`, no further reason). Existing installed Agent 1.4.2 was not modified by this task.
