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
const css = read('visionqc-v4433-clean.css');

test('v4.4.37 assets and the supplied TOPTEC logo are exact', () => {
  assert.match(html, /VisionQC DirectExport v4\.4\.37/);
  assert.match(html, /assets\/index-v4\.4\.33\.js/);
  assert.match(html, /visionqc-v4433-clean\.css\?v=4\.4\.37/);
  assert.match(html, /visionqc-extension\.js\?v=4\.4\.37/);
  assert.ok(existsSync(resolve(root, 'assets/index-v4.4.33.js')));
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

test('Runtime File Load is inside Workspace Structure and calls true preload once', () => {
  const structure = js.slice(js.indexOf('function simulationWorkspaceInspectorPanel'), js.indexOf('function simPathField'));
  const output = js.slice(js.indexOf('function simulationOutputPanel'), js.indexOf('function formatDuration'));
  const load = js.slice(js.indexOf('async function loadSelectedRuntimeFiles'), js.indexOf('function workspaceStreams'));
  assert.match(structure, /id="vq43-runtime-file-load"/);
  assert.doesNotMatch(output, /id="vq43-runtime-file-load"/);
  assert.match(load, /\/api\/runtime\/preload/);
  assert.doesNotMatch(load, /inspectSimulationWorkspace\(/);
  assert.match(load, /RUNTIME_PRELOAD_TIMEOUT_MS/);
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
  assert.doesNotMatch(js, />구조 안내</);
  const stop = js.slice(js.indexOf('async function stopSimulationAgent'), js.indexOf('async function checkSimulationRuntime'));
  assert.match(stop, /\/api\/agent\/exit/);
  assert.doesNotMatch(stop, /agent\/unregister/);
});

test('tool deletion flushes live checkbox state and Progress Update updates Batch', () => {
  const remove = js.slice(js.indexOf('function removeSelectedSimulationTools'), js.indexOf('function resetSimulationTools'));
  assert.match(remove, /flushSimulationControls\(\)/);
  assert.match(remove, /data-sim-tool-field="selected"\]:checked/);
  const sync = js.slice(js.indexOf('function syncSimulationField'), js.indexOf('function syncSimulationActiveCheckbox'));
  assert.match(sync, /field === 'printEvery'/);
  assert.match(sync, /vq43-sim-batch/);
});

test('Simulation normalization preserves edited parameters and Tool arrays at runtime', () => {
  const start = js.indexOf('function mergeSimulationSection');
  const end = js.indexOf('function ensureSimulationForm', start);
  assert.ok(start >= 0 && end > start);
  const merge = new Function(`${js.slice(start, end)}; return mergeSimulationSection;`)();
  const defaults = { printEvery:100, jpegQuality:80, tools:[{toolName:'Default'}] };
  const legacy = { printEvery:50 };
  const current = { printEvery:3, jpegQuality:91, tools:[{toolName:'UserTool',selected:true}] };
  const result = merge(defaults, legacy, current);
  assert.equal(result.printEvery, 3);
  assert.equal(result.jpegQuality, 91);
  assert.deepEqual(result.tools, current.tools);
  assert.equal(defaults.printEvery, 100);
  assert.equal(current.printEvery, 3);
  const ensure = js.slice(js.indexOf('function ensureSimulationForm'), js.indexOf('function simulationActivePositions'));
  assert.doesNotMatch(ensure, /Object\.assign\(form\.green[^\n]*defaults\.green/);
  assert.match(ensure, /if \(!Array\.isArray\(form\.green\.tools\)\)/);
  assert.doesNotMatch(ensure, /!form\.green\.tools\.length/);
});

test('Tool add/remove performs one state flush and never restores defaults implicitly', () => {
  const add = js.slice(js.indexOf('function addSimulationTool'), js.indexOf('function removeSelectedSimulationTools'));
  const remove = js.slice(js.indexOf('function removeSelectedSimulationTools'), js.indexOf('function resetSimulationTools'));
  assert.ok(add.indexOf('flushSimulationControls()') < add.indexOf('ensureSimulationForm()'));
  assert.match(add, /form\.green\.tools\.push/);
  assert.match(remove, /form\.green\.tools = next/);
  assert.doesNotMatch(remove, /next\.length \? next : simulationDefaultTools/);
  assert.match(remove, /개 Tool을 제거했습니다/);
});

test('picker requests are single-flight in Web and never queue silently', () => {
  assert.match(js, /simulationPickerPending: false/);
  const picker = js.slice(js.indexOf('async function requestSimulationPicker'), js.indexOf('function createPositionKey'));
  assert.match(picker, /if \(state\.simulationPickerPending\)/);
  assert.match(picker, /state\.simulationPickerPending = true/);
  assert.match(picker, /resetSimulationPickerState\(\)/);
  assert.match(picker, /이미 파일 또는 폴더 선택 창이 열려 있습니다/);
  assert.match(picker, /clientId:PICKER_CLIENT_ID/);
  assert.match(picker, /data\?\.busy && data\?\.recoverable/);
  assert.match(picker, /\/api\/pick\/cancel/);
});

test('Keyword Input Root keeps its contextual disabled state across global locks', () => {
  const lock = js.slice(js.indexOf('function applySimulationLockDom'), js.indexOf('async function agentFetch'));
  const sync = js.slice(js.indexOf('function syncSimulationField'), js.indexOf('function syncSimulationActiveCheckbox'));
  assert.match(js, /data-vq-base-disabled="\$\{disabled\?'1':'0'\}"/);
  assert.match(lock, /const contextDisabled = el\.dataset\.vqBaseDisabled === '1'/);
  assert.match(lock, /el\.disabled = contextDisabled \|\| running \|\| loading/);
  assert.match(sync, /refreshSimulationOptionsOnly\(\)/);
  assert.match(sync, /refreshSimulationPositionListOnly\(\)/);
});

test('Agent disconnect clears loaded Workspace memory and UI data but keeps chosen paths', () => {
  const clear = js.slice(js.indexOf('function clearSimulationLoadedWorkspaces'), js.indexOf('function prepareLiveSimulationData'));
  const poll = js.slice(js.indexOf('async function pollSimulationAgentStatus'), js.indexOf('function startSimulationAgentMonitor'));
  const stop = js.slice(js.indexOf('async function stopSimulationAgent'), js.indexOf('async function checkSimulationRuntime'));
  assert.match(clear, /position\.greenWorkspaceInfo = null/);
  assert.match(clear, /position\.blueWorkspaceInfo = null/);
  assert.match(clear, /workspaceInspectCache\.clear\(\)/);
  assert.match(clear, /workspaceInspectStatus\.clear\(\)/);
  assert.doesNotMatch(clear, /greenWorkspacePath\s*=/);
  assert.doesNotMatch(clear, /blueWorkspacePath\s*=/);
  assert.match(poll, /clearSimulationLoadedWorkspaces\(\{ render:true \}\)/);
  assert.match(stop, /markSimulationAgentOffline\(/);
});

test('Simulation Start is single-flight and verifies the live preload identity', () => {
  const start = js.slice(js.indexOf('async function startSimulation()'), js.indexOf('async function stopSimulation()'));
  assert.match(start, /simulationStartPending/);
  assert.match(start, /simulationRuntimeSignature\(request\)/);
  assert.match(start, /simulationRuntimeToken/);
  assert.match(start, /simulationRuntimeAgentInstance/);
  assert.match(start, /Runtime File Load를 다시 실행하세요/);
  assert.match(js, /runtimePreloadSignature:String\(data\.runtimePreloadSignature/);
});

test('Web preload signature matches Agent ordering and Windows path normalization', () => {
  const pathStart = js.indexOf('function runtimeSignaturePath');
  const pathEnd = js.indexOf('function clearSimulationRuntimeReadiness', pathStart);
  assert.ok(pathStart >= 0 && pathEnd > pathStart);
  const signature = new Function(`${js.slice(pathStart, pathEnd)}; return simulationRuntimeSignature;`)();
  const value = signature({
    mode:'green', green:{useGpu:true,gpuDevices:'0'}, blue:{},
    positions:[
      {key:'CA_TOP',greenWorkspacePath:'H:/Runtime/CA.vrws'},
      {key:'AN_TOP',greenWorkspacePath:'h:\\Runtime\\AN.vrws'}
    ]
  });
  assert.equal(value, 'green|True|0|AN_TOP|G:H:\\RUNTIME\\AN.VRWS|CA_TOP|G:H:\\RUNTIME\\CA.VRWS');
});

test('notification rail, unread badge, large toast, and tooltips are present', () => {
  assert.match(js, /vq43-notification-count/);
  assert.match(js, /function openNotificationCenter\(\)/);
  assert.match(js, /item\.read = true/);
  assert.match(js, /SIMULATION_TOOLTIPS/);
  assert.match(js, /applySimulationTooltips\(shell\)/);
  assert.match(css, /#vq43-toast\{[^}]*max-width:620px!important/);
  assert.match(css, /\.vq43-notification-count/);
  assert.match(css, /#vq43-param-tooltip/);
  const bottom = js.slice(js.indexOf('<div class="vq43-rail-bottom"'), js.indexOf('</div>\n        </aside>'));
  assert.ok(bottom.indexOf("railIconSvg('bell')") < bottom.indexOf("railIconSvg('theme')"));
  assert.match(bottom, /railIconSvg\('language'\)/);
  assert.match(js, /main:'<rect/);
  assert.match(js, /analysis:'<path/);
  assert.match(js, /classification:'<rect/);
});

test('picker stays open until the user completes or cancels it', () => {
  assert.match(js, /PICKER_POLL_INTERVAL_MS = 600/);
  const picker = js.slice(js.indexOf('async function requestSimulationPicker'), js.indexOf('async function browseSimulationPath'));
  assert.match(picker, /while \(true\)/);
  assert.match(picker, /timeout:4000/);
  assert.doesNotMatch(picker, /timeout:120000/);
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
