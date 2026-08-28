import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

const js = fs.readFileSync(new URL('../visionqc-extension.js', import.meta.url), 'utf8');
const css = fs.readFileSync(new URL('../visionqc-extension.css', import.meta.url), 'utf8');
const dto = fs.readFileSync(new URL('../LocalAgent_v0.2.12/AgentDtos.cs', import.meta.url), 'utf8');
const server = fs.readFileSync(new URL('../LocalAgent_v0.2.12/AgentServer.cs', import.meta.url), 'utf8');
const store = fs.readFileSync(new URL('../LocalAgent_v0.2.12/Persistence/SqliteRunStore.cs', import.meta.url), 'utf8');

test('main dashboard is compact and daily NG rate is a direct percentage line chart', () => {
  assert.match(js, /applyMainDashboardLayout\(\)/);
  assert.match(js, /vq43-main-dashboard/);
  assert.match(js, /function historyDateBars\(daily\)/);
  assert.match(js, /rateText\(rate\)/);
  assert.match(css, /grid-template-columns:minmax\(520px,.92fr\)/);
  assert.match(css, /\.vq43-history-line polyline/);
});

test('analysis dropdown and classification viewport survive live navigation updates', () => {
  assert.match(js, /analysisDropdownOpenKind/);
  assert.match(js, /isAnalysisDropdownOpen\(\)/);
  assert.match(js, /renderAnalysisPreserveDropdown\(\)/);
  assert.doesNotMatch(js, /classificationViewportSnapshot|installClassificationViewportGuard/);
  assert.match(js, /vq43-hidden-duplicate-organize/);
  assert.match(css, /\.vq43-hidden-duplicate-organize\{display:none!important\}/);
});

test('viewer overlays do not move the image and preserve zoom and pan between views', () => {
  assert.match(js, /vq43-modal-media-layer/);
  assert.match(css, /\.vq43-modal-media-layer\{position:absolute;inset:0/);
  for (const name of ['changeModalImage', 'selectModalOriginalImage', 'selectModalCropImage', 'selectModalOverlayImage']) {
    const start = js.indexOf('function ' + name + '(');
    const end = js.indexOf('\n  }', start);
    assert.ok(start >= 0 && end > start, name + ' exists');
    assert.doesNotMatch(js.slice(start, end), /resetModalView\(\)/, name + ' preserves transform');
  }
  assert.doesNotMatch(js, /Green Heatmap 생성/);
});

test('actual NG images move to a recoverable DELET folder only after confirmation', () => {
  assert.match(js, /window\.confirm\('이 이미지를 실제 NG 목록에서 제외하시겠습니까/);
  assert.match(js, /getDirectoryHandle\('DELET', \{ create:true \}\)/);
  assert.match(js, /sourceParent\.removeEntry\(sourceName\)/);
  assert.match(js, /if \(name\.toUpperCase\(\) === 'DELET'\) continue/);
  assert.match(js, /image\.actualNg = true/);
});

test('history supports 10000 exact Cell IDs and DB-backed Position Tool Workspace filters', () => {
  assert.match(js, /ids\.length >= 10000/);
  assert.match(js, /data-history-field="workspaceType"/);
  assert.match(js, /data-history-field="workspaceKey"/);
  assert.match(js, />Position 결과</);
  assert.match(dto, /List<string> cellIds/);
  assert.match(dto, /AgentHistoryFilterOptions filterOptions/);
  assert.match(store, /temp_history_cell_ids/);
  assert.match(store, /workspace_type, workspace_name, workspace_key/);
  assert.match(store, /ROW_NUMBER\(\) OVER/);
  assert.match(store, /PARTITION BY UPPER\(IFNULL\(f\.cell_id,''\)\), UPPER\(IFNULL\(f\.position_key,''\)\), UPPER\(IFNULL\(f\.workspace_key,''\)\)/);
  assert.match(server, /BuildHistoryWorkspaceMap\(request\)/);
  assert.match(css, /calendar-picker-indicator\{filter:invert\(1\)/);
});
