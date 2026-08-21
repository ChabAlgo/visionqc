import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const js = readFileSync(resolve(root, 'visionqc-extension.js'), 'utf8');

test('Image Folder fields retain a normalized list of selected folders', () => {
  const start = js.indexOf('function imageRootListField');
  const end = js.indexOf('function simulationDefaults', start);
  assert.ok(start >= 0 && end > start);
  const helpers = new Function(`${js.slice(start, end)}; return { imageRootListField, normalizeImageRoots, imageRootsForPosition, setImageRootsForPosition };`)();
  const position = { greenImageRoot:'C:\\old', greenImageRoots:[] };
  assert.deepEqual(helpers.imageRootsForPosition(position, 'greenImageRoot'), ['C:\\old']);
  assert.deepEqual(helpers.setImageRootsForPosition(position, 'greenImageRoot', ['C:\\A', 'C:\\B', 'c:\\a']), ['C:\\A', 'C:\\B']);
  assert.equal(position.greenImageRoot, 'C:\\A');
  assert.deepEqual(position.greenImageRoots, ['C:\\A', 'C:\\B']);
});

test('Image Folder browse requests and Simulation payload include all selected folders', () => {
  const browse = js.slice(js.indexOf('async function browseSimulationPath'), js.indexOf('function createPositionKey'));
  const request = js.slice(js.indexOf('function buildSimulationRequest'), js.indexOf('function runtimeSignaturePath'));
  const field = js.slice(js.indexOf('function simPathField'), js.indexOf('function simulationPositionRows'));
  assert.match(browse, /multiple:imageFolderList/);
  assert.match(browse, /setImageRootsForPosition\(currentTarget, field, selectedPaths\)/);
  assert.match(request, /greenImageRoots:imageRootsForPosition/);
  assert.match(request, /blueImageRoots:imageRootsForPosition/);
  assert.match(field, /data-sim-multiple/);
  assert.match(field, /다중 선택/);
});

test('Keyword mode keeps Image Folder multi-selection available', () => {
  const rows = js.slice(js.indexOf('function simulationPositionRows'), js.indexOf('function simulationPositionToolbar'));
  assert.doesNotMatch(rows, /greenImageRoot[^\n]*'folder','folder',keywordMode/);
  assert.doesNotMatch(rows, /blueImageRoot[^\n]*'folder','folder',keywordMode/);
});

test('Workspace selection updates its path input without a second UI event', () => {
  const browse = js.slice(js.indexOf('async function browseSimulationPath'), js.indexOf('function createPositionKey'));
  assert.match(browse, /if \(workspaceKind\) \{[\s\S]*?if \(input\) input\.value = selectedPaths\[0\];[\s\S]*?refreshWorkspaceInspectionUi/);
});

test('Chrome uses the loopback address space that matches the Local Agent listener', () => {
  const fetcher = js.slice(js.indexOf('async function agentFetch'), js.indexOf('async function pollSimulationAgentStatus'));
  assert.match(fetcher, /targetAddressSpace:'loopback'/);
  assert.doesNotMatch(fetcher, /targetAddressSpace:'local'/);
});

test('Chrome loopback permission denial has a clear recovery message', () => {
  const helper = js.slice(js.indexOf('async function localAgentOfflineMessage'), js.indexOf('async function pollSimulationAgentStatus'));
  const poll = js.slice(js.indexOf('async function pollSimulationAgentStatus'), js.indexOf('function startSimulationAgentMonitor'));
  assert.match(helper, /name:'loopback-network'/);
  assert.match(helper, /permission\.state === 'denied'/);
  assert.match(helper, /permission\.state === 'prompt'/);
  assert.match(poll, /await localAgentOfflineMessage\(\)/);
});

test('Agent Run click requests Chrome loopback access before launching the protocol', () => {
  const launch = js.slice(js.indexOf('function launchSimulationAgent'), js.indexOf('async function stopSimulationAgent'));
  assert.match(launch, /pollSimulationAgentStatus\(\);/);
  assert.ok(launch.indexOf('pollSimulationAgentStatus();') < launch.indexOf("window.location.href = 'visionqc-agent://start'"));
});
