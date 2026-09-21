# VisionQC 4.8.4 / Agent 1.4.4

## Problem and correction
- Agent-backed Score Analysis introduced in 4.8.1 omitted the existing upper two-column layout. Controls, KPI, export, minimum score and statistics stacked vertically, pushing charts below the screen.
- Restored the legacy presentation: filters/scope note/KPIs on the left; CSV/minimum/statistics on the right; two charts below. Restored explanatory copy, summary colors and expand-button styling.
- Agent statistics, bounded 300-point keyset pages, CSV request filters and chart interaction handlers are retained. No inspection, database or export algorithm changes.
- Agent version changes only package/version metadata so the offline installer carries the restored web UI. Prior 4.8.3 fixes remain included.

## Verification
- Added Agent-path geometry coverage at 1920x1080 in dark and light themes, requiring the original control order, two visible charts and no horizontal overflow.
- Same test checks next/previous 300-point pages and enlarged chart opening. The initial test used a class selector for an element identified by ID; corrected the test locator.
- Static checks: 144/144 passed.
- Worker build and release artifact audit passed: 17 embedded resources; Core/8.0/8.2/Universal worker bundle; no user data or Cognex DLL.
- Full browser run: 83 passed, two legacy synthetic aggregation tests adopted the real local Agent and returned remote-mode counts. Isolated those fixtures from local API access; reran both affected test files, 16/16 passed (85 distinct scenarios covered).
- Agent-backed two-column geometry, dark/light themes, 300-point next/previous navigation and expanded chart passed. Screenshots visually checked; corrected light-theme expand-button contrast.
- This presentation-only patch does not establish long-duration memory-leak or multi-million-image performance guarantees.

## Local installation constraint
- Earlier silent installer execution was rejected by automatic execution review with `blocked by policy`; no detailed reason was provided. No alternate installation bypass was attempted. Local Agent update is not claimed complete.
