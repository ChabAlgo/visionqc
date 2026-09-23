# VisionQC 4.8.7 / Agent 1.4.7 verification

## Scope
- Report daily NG graph only: all dates, chronological groups of at most 20, shared Y scale, single-day point without a line, complete chart blocks across PDF pages.
- Preserved other report markup/styles against a 4.8.6 baseline fixture. Dashboard still pages 10 dates. Full daily summaries are requested from Agent only on report export.
- Follow-up: start acknowledgement timeout increased from 10s to 60s. On timeout/network loss, query actual run state once without resending start; unknown outcome is not reported as confirmed failure. A completed recovered run is not marked running.

## Evidence
- Static: 144/144 passed before and after follow-up.
- Browser: full 91/91 passed for report change; additional five start-response tests passed, including real 11-second delayed acknowledgement, running/completed recovery, old-run rejection and unreachable Agent.
- Report cases: 0, 1, 20, 21, 40, 41, 300 days. No missing or repeated dates; non-chart HTML/style baseline unchanged.
- Native HTTP test: real built Core Worker, isolated temporary DB; default date window 10, allDates 41, exact first/last dates. Existing scoped export/count/threshold cases passed.
- 41-day PDF rendered to 3 A4 pages and visually inspected: 20 + 20 + 1 days, no graph clipping, other report sections unchanged. QA PDF: C:/Temp/vq487-41-days.pdf.
- Agent worker/installer builds passed. Release artifact audit passed (17 resources; no user DB/images or Cognex DLLs embedded).

## Limits
- Delayed startup uses controlled browser responses, not a new long GPU production run. Exact slow operation on the user's PC is not measured.
- Local installer execution was blocked by automatic execution policy in the prior attempt; no bypass attempted. Installer/package publication is distinct from installation on the user's PC.
