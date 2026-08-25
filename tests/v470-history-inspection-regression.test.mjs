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
const cleanCss = read('visionqc-v4433-clean.css');
const html = read('index.html');

test('v4.7.4 Web and Agent download targets are aligned', () => {
  assert.match(read('VERSION.txt'), /v4\.7\.4/);
  assert.match(html, /visionqc-extension\.js\?v=4\.7\.4/);
  assert.match(html, /visionqc-v470\.css\?v=4\.7\.4/);
  assert.match(js, /const VERSION = '4\.7\.4'/);
  assert.match(js, /const EXPECTED_AGENT_VERSION = '1\.2\.3'/);
  assert.match(js, /VisionQC_Agent_Installer_v1\.2\.3\.exe/);
  assert.match(js, /VisionQC_Offline_v4\.7\.4\.zip/);
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

test('AI SUGGEST uses the loaded local Runtime and tool-score viewer has explicit Heatmap controls', () => {
  assert.match(js, /function installLegacyAiSuggestRuntimeBridge/);
  assert.match(js, /\/api\/classification\/inspect-upload/);
  assert.match(js, /function buildLoadedRuntimeAiSuggestRequest/);
  assert.doesNotMatch(js, /\/api\/classification\/inspect\/auto/);
  assert.doesNotMatch(js, /navItem\('inspection', 'inspection'/);
  assert.match(js, /function overlayImagesForRecord/);
  assert.match(js, /data-vq-action="modal-overlay-image"/);
  assert.match(js, /function actualNgMinimumScore/);
  assert.match(js, /data-vq-modal-overlay/);
  assert.match(dashboardCss, /\.vq43-modal-image-switcher/);
});

test('settings uses an SVG cog icon rather than an emoji glyph', () => {
  const icon = js.slice(js.indexOf('function railIconSvg'), js.indexOf('function toggleMenu'));
  assert.match(icon, /settings:/);
  assert.match(icon, /<svg class="vq43-icon-svg" viewBox="0 0 24 24"/);
  assert.match(js, /railIconSvg\('settings'\)/);
  assert.doesNotMatch(icon, /⚙/);
});

test('theme toggle persists a true light mode and uses a split dark-white icon', () => {
  assert.match(js, /const THEME_KEY = 'visionqc-v472-theme'/);
  assert.match(js, /data-vq-action="theme-toggle"/);
  assert.match(js, /function applyTheme\(\)/);
  assert.match(js, /function toggleTheme\(\)/);
  assert.match(js, /theme:'<circle[^>]*fill="#f8fafc"/);
  assert.match(js, /fill="#111827"/);
  assert.match(cleanCss, /body\.vq43-theme-light/);
  assert.match(cleanCss, /\.vq43-theme-toggle\[aria-pressed="true"\]/);
});

test('Web accepts an Integrated Runtime only for the same GPU and Green Workspace set', () => {
  assert.match(js, /function isCompatiblePreloadedRuntime\(/);
  assert.match(js, /function simulationRuntimeControlSignature\(/);
  assert.match(js, /function simulationGreenWorkspaceSignature\(/);
  assert.match(js, /runtimePreloadMode === 'integrated'/);
  assert.match(js, /runtimePreloadControlSignature === simulationRuntimeControlSignature\(request\)/);
  assert.match(js, /runtimePreloadGreenWorkspaceSignature === simulationGreenWorkspaceSignature\(request\)/);
});

test('inline Cell score points open images and settings use compact two-column light layout', () => {
  assert.match(js, /function bindInlineScoreChartControls/);
  assert.match(js, /vq43-analysis-scatter/);
  assert.match(js, /scatterSvg\(points,\{interactive:true\}\)/);
  assert.match(cleanCss, /body\[data-vq-page="settings"\] #vq43-page>\.vq43-content\{display:grid;grid-template-columns:repeat\(2/);
  assert.match(cleanCss, /vq43-settings-card,.vq43-input-row/);
});
