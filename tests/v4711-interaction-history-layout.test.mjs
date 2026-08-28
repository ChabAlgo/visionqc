import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (rel) => fs.readFileSync(path.join(root, rel), 'utf8');
const js = read('visionqc-extension.js');
const css = read('visionqc-extension.css');
const cleanCss = read('visionqc-v4433-clean.css');
const base = read('assets/index-v4.4.33.js');
const store = read('LocalAgent_v0.2.12/Persistence/SqliteRunStore.cs');
const server = read('LocalAgent_v0.2.12/AgentServer.cs');
const dto = read('LocalAgent_v0.2.12/AgentDtos.cs');

test('classification relies on the native generation-safe focal restoration only', () => {
  assert.doesNotMatch(js, /classificationViewportSnapshot|installClassificationViewportGuard|setTimeout\(restore, 90\)/);
  assert.ok(base.includes('VQ42_zoomRef'));
  assert.ok(base.includes('oe.current={xRatio:'));
  assert.ok(base.includes('[i.id]'));
});

test('analysis live render preserves an open dropdown instead of freezing updates', () => {
  assert.ok(js.includes('function renderAnalysisPreserveDropdown()'));
  assert.ok(js.includes("restored.classList.add('open')"));
  assert.ok(js.includes('function queueLiveUiRender()'));
  assert.match(js, /if \(isAnalysisDropdownOpen\(\)\) \{\s*state\.analysisLiveRenderPending = true;/);
  assert.match(js, /state\.page === 'main' \|\| state\.page === 'analysis'\) queueLiveUiRender\(\)/);
});

test('viewer arrow keys navigate four directions and retain modal transform state', () => {
  assert.ok(js.includes("['ArrowLeft','ArrowUp','ArrowRight','ArrowDown'].includes(event.key)"));
  assert.ok(js.includes("changeModalImage(event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 1)"));
  const change = js.slice(js.indexOf('function changeModalImage'), js.indexOf('function selectModalOriginalImage'));
  assert.doesNotMatch(change, /modalZoom\s*=|modalPanX\s*=|modalPanY\s*=/);
});

test('main dashboard swaps Cell and history, fits cards, and removes internal scroll', () => {
  assert.ok(css.includes('>.vq43-main-cell{grid-column:1;grid-row:2}'));
  assert.ok(css.includes('>.vq43-main-history-dashboard{grid-column:2;grid-row:2}'));
  assert.match(css, /\.vq43-main-dashboard \.vq43-position-grid\{[^}]*max-height:none!important;overflow:visible!important/);
  assert.match(css, /\.vq43-main-dashboard \.vq43-tool-position-grid\{[^}]*max-height:none!important;overflow:visible!important/);
  assert.match(css, /\.vq43-main-dashboard \.vq43-threshold-input\{[^}]*width:48px!important/);
});

test('dark calendar and VPDL Worker controls have explicit themed styles', () => {
  assert.ok(css.includes('calendar-picker-indicator{filter:brightness(0) invert(1)!important'));
  assert.ok(cleanCss.includes('calendar-picker-indicator{filter:brightness(0) invert(1)!important'));
  assert.ok(js.includes('vq43-vpdl-worker-copy'));
  assert.ok(js.includes('DLL 충돌을 방지합니다'));
  assert.ok(css.includes('body.vq43-theme-light #vq43-shell .vq43-vpdl-worker-switch'));
});

test('history stores mode workspace and web version and uses indexed window dedupe', () => {
  assert.ok(dto.includes('public string webVersion { get; set; }'));
  assert.ok(js.includes("mode:state.simulationMode || 'integrated', webVersion:VERSION"));
  assert.ok(server.includes('WebVersion = request.webVersion ?? ""'));
  assert.ok(server.includes('WorkspacesByPosition = BuildHistoryWorkspaceMap(request)'));
  assert.ok(store.includes('ROW_NUMBER() OVER'));
  assert.ok(store.includes('idx_images_history_dedupe'));
  assert.ok(store.includes('UPDATE images SET workspace_type='));
});
