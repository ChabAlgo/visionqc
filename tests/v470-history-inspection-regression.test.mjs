import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const read = (path) => readFileSync(resolve(root, path), 'utf8');
const js = read('visionqc-extension.js');
const css = read('visionqc-extension.css');
const dashboardCss = read('visionqc-v470.css');
const html = read('index.html');

test('v4.7.0 Web and Agent download targets are aligned', () => {
  assert.match(read('VERSION.txt'), /v4\.7\.0/);
  assert.match(html, /visionqc-extension\.js\?v=4\.7\.0/);
  assert.match(html, /visionqc-v470\.css\?v=4\.7\.0/);
  assert.match(js, /const VERSION = '4\.7\.0'/);
  assert.match(js, /const EXPECTED_AGENT_VERSION = '1\.2\.0'/);
  assert.match(js, /VisionQC_Agent_Installer_v1\.2\.0\.exe/);
  assert.match(js, /VisionQC_Offline_v4\.7\.0\.zip/);
});

test('persistent History page has filters, server-side pagination, daily NG chart and image viewer', () => {
  assert.match(js, /navItem\('history', 'history'/);
  assert.match(js, /function renderHistory\(\)/);
  assert.match(js, /data-history-field="fromDate"/);
  assert.match(js, /data-history-field="cellId"/);
  assert.match(js, /\/api\/history\/search/);
  assert.match(js, /\/api\/history\/import-file\/start/);
  assert.match(js, /\/api\/history\/import-file\/status/);
  assert.match(js, /function openHistoryImage/);
  assert.match(js, /Heatmap Overlay/);
  assert.match(css, /\.vq43-history-kpis/);
  assert.match(css, /\.vq43-history-bars/);
  assert.match(js, /function mainHistoryDashboardPanel\(\)/);
  assert.match(js, /data-vq-action="history-open"/);
  assert.match(dashboardCss, /\.vq43-main-history-dashboard/);
});

test('AI inspection page resolves a filename to a Green workspace and toggles stored overlay paths', () => {
  assert.match(js, /navItem\('inspection', 'inspection'/);
  assert.match(js, /function inspectionRequest\(\)/);
  assert.match(js, /\/api\/classification\/inspect\/auto/);
  assert.match(js, /function toggleInspectionOverlay/);
  assert.match(js, /function loadInspectionPreview/);
  assert.match(js, /heatmapImageSave = !!state\.inspectionHeatmapEnabled/);
  assert.match(css, /\.vq43-inspection-preview/);
  assert.match(css, /\.vq43-inspection-tool/);
});

test('settings uses an SVG cog icon rather than an emoji glyph', () => {
  const icon = js.slice(js.indexOf('function railIconSvg'), js.indexOf('function toggleMenu'));
  assert.match(icon, /settings:/);
  assert.match(icon, /<svg class="vq43-icon-svg" viewBox="0 0 24 24"/);
  assert.match(js, /railIconSvg\('settings'\)/);
  assert.doesNotMatch(icon, /⚙/);
});
