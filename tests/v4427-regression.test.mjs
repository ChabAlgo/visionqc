import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const read = (path) => readFileSync(resolve(root, path), 'utf8');
const html = read('index.html');
const js = read('visionqc-extension.js');
const css = read('visionqc-v4427-clean.css');

test('v4.4.27 assets and the supplied TOPTEC logo are exact', () => {
  assert.match(html, /VisionQC DirectExport v4\.4\.27/);
  assert.match(html, /assets\/index-v4\.4\.27\.js/);
  assert.match(html, /visionqc-v4427-clean\.css\?v=4\.4\.27/);
  assert.match(html, /visionqc-extension\.js\?v=4\.4\.27/);
  assert.ok(existsSync(resolve(root, 'assets/index-v4.4.27.js')));
  const logo = readFileSync(resolve(root, 'assets/toptec-logo.png'));
  assert.equal(createHash('sha256').update(logo).digest('hex'), 'ab35afda21bd2d40052b79ca46b75613096f60a7b8a84d6112ccd25daa5aa4a4');
});

test('FHD rail and Simulation geometry remain authoritative', () => {
  assert.match(css, /--vq-rail-open:272px/);
  assert.match(css, /grid-template-columns:minmax\(0,1fr\) var\(--vq-sim-options-w\)!important/);
  assert.match(css, /@media\(max-width:1439px\)\{\.vq43-sim-layout\{grid-template-columns:minmax\(0,1fr\)!important/);
  assert.match(css, /body\.vq43-menu-expanded #vq43-shell\{left:var\(--vq-rail-open\)!important/);
});

test('Workspace paths wait for the explicit Runtime File Load action', () => {
  assert.match(js, /data-vq-action="simulation-runtime-load"/);
  assert.match(js, /async function loadSelectedRuntimeFiles\(\)/);
  assert.match(js, /Runtime File Load를 먼저 완료하세요/);
  assert.doesNotMatch(js, /resumeStoredWorkspaceInspections/);
  const browse = js.slice(js.indexOf('async function browseSimulationPath'), js.indexOf('function createPositionKey'));
  assert.doesNotMatch(browse, /await inspectSimulationWorkspace/);
  assert.match(browse, /clearWorkspaceInspectStatus\(key, workspaceKind, false\)/);
});

test('Workspace async results write to the live Position object', () => {
  assert.match(js, /Object\.assign\(p, \{/);
  assert.match(js, /const currentPosition = currentForm\.positions\?\.\[positionKey\]/);
  assert.match(js, /currentPosition\.greenWorkspaceInfo = data/);
  assert.match(js, /currentPosition\.blueWorkspaceInfo = data/);
  assert.doesNotMatch(js, /p\.greenWorkspaceInfo = data/);
});

test('Workspace cards merge Green and Blue under one Position', () => {
  const panel = js.slice(js.indexOf('function simulationWorkspaceInspectorPanel'), js.indexOf('function simPathField'));
  assert.match(panel, /vq43-workspace-position-card/);
  assert.match(panel, /vq43-workspace-kind-grid/);
  assert.match(panel, /kind:'green', title:'Green'/);
  assert.match(panel, /kind:'blue', title:'Blue'/);
  assert.doesNotMatch(panel, /vq43-workspace-card /);
  assert.match(css, /\.vq43-workspace-kind-grid\{display:grid;grid-template-columns:repeat\(2,minmax\(0,1fr\)\)/);
});

test('Output is followed by Runtime Structure before Simulation Status', () => {
  const render = js.slice(js.indexOf('function renderSimulation()'), js.indexOf('function renderSettings()'));
  const output = render.indexOf('${simulationOutputPanel()}');
  const structure = render.indexOf('${simulationWorkspaceInspectorPanel()}');
  const status = render.indexOf('${simulationStatusPanel()}');
  assert.ok(output >= 0 && structure > output && status > structure);
});

test('Simulation Start requires preload and rechecks Runtime License', () => {
  const start = js.slice(js.indexOf('async function startSimulation()'), js.indexOf('async function stopSimulation()'));
  assert.match(start, /requiredSimulationWorkspaceTargets\(\)/);
  assert.match(start, /!target\.info\?\.ok/);
  assert.match(start, /workspacePathMatches\(target\.info\.path, target\.path\)/);
  assert.match(start, /checkSimulationRuntime\(\{ silent:true, reason:'simulation-start' \}\)/);
  assert.ok(start.indexOf('checkSimulationRuntime') < start.indexOf('/api/simulation/start'));
});

test('Agent is monitored every two seconds with launch and exit only', () => {
  assert.match(js, /window\.setInterval\(pollSimulationAgentStatus, 2000\)/);
  assert.match(js, /startSimulationAgentMonitor\(\)/);
  assert.match(js, /data-vq-action="simulation-agent-stop"[^>]*>Agent 종료</);
  assert.doesNotMatch(js, /data-vq-action="simulation-agent-check"/);
  assert.doesNotMatch(js, />연결 확인</);
  assert.doesNotMatch(js, />Agent 제거</);
  const stop = js.slice(js.indexOf('async function stopSimulationAgent'), js.indexOf('async function checkSimulationRuntime'));
  assert.match(stop, /\/api\/agent\/exit/);
  assert.doesNotMatch(stop, /agent\/unregister/);
});

test('License checks automatically on Agent detection and Simulation Start', () => {
  const poll = js.slice(js.indexOf('async function pollSimulationAgentStatus'), js.indexOf('function startSimulationAgentMonitor'));
  assert.match(poll, /checkSimulationRuntime\(\{ silent:true, reason:'agent-start' \}\)/);
  assert.doesNotMatch(js, /Runtime \/ License 실제 확인/);
});

test('Fallback picker and preview re-resolve stable rows after awaits', () => {
  assert.match(js, /const slotKey = row\.slotKey/);
  assert.match(js, /fallbacks\.find\(\(item\) => item\.slotKey === slotKey\)/);
  assert.match(js, /fileType:'image'/);
  assert.match(js, /data-vq-action="simulation-fallback-preview"/);
  assert.match(js, /<div class="vq43-fallback-sample vq43-sim-option-field">/);
  assert.doesNotMatch(js, /<label class="vq43-fallback-sample"/);
});

test('Fallback controls match the standard option field sizing', () => {
  assert.match(css, /fallback-metrics \.vq43-sim-option-field>span[\s\S]*?font-size:12px!important/);
  assert.match(css, /fallback-metrics \.vq43-sim-option-field input[\s\S]*?height:32px!important;min-height:32px!important/);
  assert.match(css, /fallback-sample\.vq43-sim-option-field input[\s\S]*?font-size:14px!important/);
  assert.match(css, /\.vq43-sim-preview-dialog\{[^}]*overflow-x:hidden!important/);
});

test('Tool Settings and Fallback stay free of horizontal dragging', () => {
  assert.match(css, /\.vq43-sim-tools-wrap\{overflow-x:hidden!important\}/);
  assert.match(css, /\.vq43-sim-table\.tools\{[^}]*table-layout:fixed!important/);
  assert.match(css, /\.vq43-fallback-list\{[^}]*overflow-x:hidden!important/);
  assert.doesNotMatch(js, /class="vq43-sim-table fallback"/);
});

test('Logs and text selection keep the prior regression fixes', () => {
  assert.match(js, /function formatSimulationLogTime\(value\)/);
  assert.doesNotMatch(js, /toLocaleTimeString/);
  assert.match(css, /\.vq43-sim-page,\.vq43-sim-page \*\{[^}]*user-select:text!important/);
  const guard = js.slice(js.indexOf('function installInteractionGuards'), js.indexOf('function navItem'));
  assert.doesNotMatch(guard, /preventDefault|stopPropagation|stopImmediatePropagation/);
});

test('FHD browser harness still covers scroll, selection, and overflow', () => {
  const harness = read('tests/browser-regression.html');
  const spec = read('tests/browser-regression.spec.mjs');
  assert.match(harness, /width:1920px;height:1080px/);
  assert.match(spec, /page\.mouse\.down\(\)/);
  assert.match(spec, /selectedOnRelease/);
  assert.match(js, /fallbackNoHorizontal/);
});

test('authoritative stylesheet has balanced braces', () => {
  const stripped = css.replace(/\/\*[\s\S]*?\*\//g, '');
  assert.equal([...stripped].filter((char) => char === '{').length, [...stripped].filter((char) => char === '}').length);
});
