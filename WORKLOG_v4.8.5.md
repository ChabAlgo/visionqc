# VisionQC 4.8.5 / Agent 1.4.5

## Root cause and correction
- Every dashboard revision invalidated the Score key. The renderer replaced the entire Score screen with a separate full-width loading layout until the asynchronous statistics/window request completed. This also happened during live simulation polling.
- Retain the last complete frame for the same Score query while fetching a new revision; show a status message and replace results only after the request completes. Initial loading uses the original two-column layout too.
- Dashboard refresh no longer rebuilds the unrelated Simulation or Settings page. No engine, projection, Threshold or CSV algorithm changes.
- Rename result input controls to CSV selection / Excel selection. Explicit tooltips now bind outside Simulation too and support pointer hover plus keyboard focus. Existing fallback file loading and 50MB limit are retained.

## Verification
- Delayed Agent responses across three live refresh cycles: the original Score frame and graph nodes remain mounted while requests wait; fresh counts appear after completion.
- Save/reload restores the same Score layout; monitoring checks for missing two-column structure. Live analysis refresh preserves the Simulation page node.
- CSV and Excel tooltip hover/focus checks, dark/light Score layout, page navigation, bounded browser data and scoped CSV regression coverage.
- Static: 144/144 passed. Full browser: 87/87 passed (3.2 minutes).
- Native Agent HTTP selection export passed: selected date/Position/Tool has 17 NG at initial Threshold and 10 at 0.80; history export, split multi-CSV reimport and empty selection also pass. Isolated temporary data: C:/Users/HP/AppData/Local/Temp/VisionQC-482-selection-LFFKTl.
- Release artifact audit: 17 embedded resources, Core/8.0/8.2/Universal workers, no user data/Cognex DLL included.
- Live UI timing tests use controlled Agent responses, not a long-duration production GPU simulation or a memory-leak proof.
- Local automatic installation remains uncompleted after the earlier automatic execution review rejection (`blocked by policy`, no detailed reason). No installation bypass attempted.
