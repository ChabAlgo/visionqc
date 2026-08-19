import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const read = (path) => readFileSync(resolve(root, path), 'utf8');
const html = read('index.html');
const js = read('visionqc-extension.js');
const css = read('visionqc-v4425-clean.css');

test('v4.4.25 assets are versioned and loaded in override order', () => {
  assert.match(html, /VisionQC DirectExport v4\.4\.25/);
  assert.match(html, /assets\/index-v4\.4\.25\.js/);
  assert.match(html, /visionqc-extension\.js\?v=4\.4\.25/);
  assert.match(html, /visionqc-v4425-clean\.css\?v=4\.4\.25/);
  assert.ok(html.indexOf('visionqc-v4425-clean.css') > html.indexOf('visionqc-extension.css'));
  assert.ok(existsSync(resolve(root, 'assets/index-v4.4.25.js')));
  assert.ok(existsSync(resolve(root, 'visionqc-v4425-clean.css')));
});

test('expanded drawer shifts the FHD workspace instead of covering it', () => {
  assert.match(css, /--vq-rail-open:272px/);
  assert.match(css, /body\.vq43-menu-expanded #vq43-shell\{left:var\(--vq-rail-open\)!important/);
  assert.match(css, /body\.vq43-menu-expanded\[data-vq-page="classification"\]/);
  assert.match(css, /transition:left \.2s ease,width \.2s ease/);
});

test('Simulation Options uses a viewport-fixed outer panel and dedicated stateful scroller', () => {
  assert.match(js, /class="vq43-sim-options-scroll"/);
  assert.match(js, /simulationOptionsScrollTop/);
  assert.match(js, /bindSimulationOptionsScrollState/);
  assert.match(js, /optionsTopBeforeOutsidePointer/);
  assert.match(js, /document\.addEventListener\('pointerup', restoreAfterOutsidePointer, true\)/);
  assert.match(css, /\.vq43-sim-options\{[^}]*position:fixed!important/);
  assert.match(css, /\.vq43-sim-options\{[^}]*overflow:hidden!important/);
  assert.match(css, /\.vq43-sim-options-scroll\{[^}]*overflow-y:auto!important/);
  assert.match(css, /overflow-anchor:none!important/);
  assert.doesNotMatch(css, /contain:layout paint/);
});

test('Tool Settings fits its panel without horizontal dragging', () => {
  assert.match(js, /class="vq43-sim-table-wrap vq43-sim-tools-wrap"/);
  assert.match(js, /class="vq43-tool-col-threshold"/);
  assert.match(css, /\.vq43-sim-tools-wrap\{overflow-x:hidden!important\}/);
  assert.match(css, /\.vq43-sim-table\.tools\{[^}]*table-layout:fixed!important/);
  assert.match(css, /\.vq43-sim-table\.tools\{[^}]*min-width:0!important/);
  assert.doesNotMatch(css, /\.vq43-sim-table\.tools\{min-width:620px!important\}/);
});

test('Fallback and Preview no longer require a wide horizontal table', () => {
  assert.match(js, /class="vq43-fallback-list"/);
  assert.match(js, /class="vq43-fallback-card"/);
  assert.doesNotMatch(js, /class="vq43-sim-table fallback"/);
  assert.doesNotMatch(css, /\.vq43-sim-table\.fallback\{min-width:980px!important\}/);
  assert.match(css, /\.vq43-fallback-metrics\{[^}]*grid-template-columns:repeat\(3,minmax\(0,1fr\)\)!important/);
  assert.match(css, /\.vq43-sim-preview-dialog\{[^}]*overflow-x:hidden!important/);
});

test('FHD stays two-column and narrower viewports fall back to one column', () => {
  assert.match(css, /grid-template-columns:minmax\(0,1fr\) var\(--vq-sim-options-w\)!important/);
  assert.match(css, /@media\(max-width:1439px\)\{\.vq43-sim-layout\{grid-template-columns:minmax\(0,1fr\)!important/);
});

test('Agent status refreshes do not replace the complete Simulation DOM', () => {
  const block = js.slice(js.indexOf('async function checkSimulationAgent'), js.indexOf('function launchSimulationAgent'));
  assert.ok(block.includes('updateSimulationAgentDom()'));
  assert.ok(!block.includes('renderSimulationPreserveScroll()'));
  assert.ok(!block.includes('renderSimulation()'));
});

test('Workspace structure requests are deduplicated and update only their targeted controls', () => {
  assert.match(js, /const workspaceInspectInflight = new Map\(\)/);
  assert.match(js, /workspaceInspectInflight\.has\(requestKey\)/);
  assert.match(js, /refreshWorkspaceInspectionUi\(positionKey, kind\)/);
  const refreshBlock = js.slice(js.indexOf('function refreshWorkspaceInspectionUi'), js.indexOf('function workspaceInspectRequestKey'));
  assert.ok(!refreshBlock.includes('list.innerHTML = simulationPositionRows()'));
  assert.match(refreshBlock, /data-vq-workspace-summary/);
  assert.match(refreshBlock, /data-vq-workspace-select/);
});

test('Simulation text selection remains native and interaction guards never cancel pointer defaults', () => {
  assert.match(css, /\.vq43-sim-page,\.vq43-sim-page \*\{[^}]*user-select:text!important;-webkit-user-select:text!important\}/);
  assert.match(css, /\.vq43-sim-page input:not\(\[type="checkbox"\]\):not\(\[type="radio"\]\)[^}]*user-select:text!important/);
  const guardBlock = js.slice(js.indexOf('function installInteractionGuards'), js.indexOf('function navItem'));
  assert.ok(!guardBlock.includes('preventDefault'));
  assert.ok(!guardBlock.includes('stopPropagation'));
  assert.ok(!guardBlock.includes('stopImmediatePropagation'));
  const clickBlock = js.slice(js.indexOf('function handleDelegatedClick'), js.indexOf('function handleDelegatedChange'));
  assert.match(clickBlock, /\.vq43-nav-item\[data-vq-page\]/);
  assert.ok(!clickBlock.includes("closest?.('[data-vq-page]')"));
  assert.match(js, /simulationDomPreserved/);
});

test('Web requires the serialized-control Agent v0.2.4', () => {
  assert.match(js, /EXPECTED_AGENT_VERSION = '0\.2\.4'/);
});

test('FHD browser regression harness covers scroll, selection, and horizontal overflow', () => {
  const harness = read('tests/browser-regression.html');
  const spec = read('tests/browser-regression.spec.mjs');
  const workflow = read('.github/workflows/browser-regression.yml');
  assert.match(js, /runSimulationUiRegression\(\)/);
  assert.match(js, /openSimulationPreview\(\)/);
  assert.match(js, /scrollPreserved/);
  assert.match(js, /selectionPreserved/);
  assert.match(js, /fallbackNoHorizontal/);
  assert.match(harness, /width:1920px;height:1080px/);
  assert.match(harness, /debug\.runSimulationUiRegression\(\)/);
  assert.match(spec, /page\.mouse\.down\(\)/);
  assert.match(spec, /page\.mouse\.up\(\)/);
  assert.match(spec, /selectedOnRelease/);
  assert.match(spec, /scrollWidth - element\.clientWidth/);
  assert.match(workflow, /Chromium 1920x1080/);
  assert.match(workflow, /playwright install --with-deps chromium/);
});

test('authoritative stylesheet has balanced braces', () => {
  const stripped = css.replace(/\/\*[\s\S]*?\*\//g, '');
  const opens = [...stripped].filter((char) => char === '{').length;
  const closes = [...stripped].filter((char) => char === '}').length;
  assert.equal(opens, closes);
});
