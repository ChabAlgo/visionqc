import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const js = readFileSync(resolve(root, 'visionqc-extension.js'), 'utf8');

test('Image Folder and Keyword Input Root fields retain normalized selected folder lists', () => {
  const start = js.indexOf('function imageRootListField');
  const end = js.indexOf('function simulationDefaults', start);
  assert.ok(start >= 0 && end > start);
  const helpers = new Function(`${js.slice(start, end)}; return { imageRootListField, normalizeImageRoots, imageRootsForPosition, setImageRootsForPosition, isMultiFolderSelectionField, simulationFolderRoots, setSimulationFolderRoots };`)();
  const position = { greenImageRoot:'C:\\old', greenImageRoots:[] };
  assert.deepEqual(helpers.imageRootsForPosition(position, 'greenImageRoot'), ['C:\\old']);
  assert.deepEqual(helpers.setImageRootsForPosition(position, 'greenImageRoot', ['C:\\A', 'C:\\B', 'c:\\a']), ['C:\\A', 'C:\\B']);
  assert.equal(position.greenImageRoot, 'C:\\A');
  assert.deepEqual(position.greenImageRoots, ['C:\\A', 'C:\\B']);
  const keyword = { keywordInputRoot:'C:\\old', keywordInputRoots:[] };
  assert.equal(helpers.isMultiFolderSelectionField('integrated', 'keywordInputRoot'), true);
  assert.deepEqual(helpers.setSimulationFolderRoots(keyword, 'integrated', 'keywordInputRoot', ['C:\\A', 'C:\\B', 'c:\\a']), ['C:\\A', 'C:\\B']);
  assert.equal(keyword.keywordInputRoot, 'C:\\A');
  assert.deepEqual(keyword.keywordInputRoots, ['C:\\A', 'C:\\B']);
});

test('Image Folder browse requests and Simulation payload include all selected folders', () => {
  const browse = js.slice(js.indexOf('async function browseSimulationPath'), js.indexOf('function createPositionKey'));
  const request = js.slice(js.indexOf('function buildSimulationRequest'), js.indexOf('function runtimeSignaturePath'));
  const field = js.slice(js.indexOf('function simPathField'), js.indexOf('function simulationPositionRows'));
  assert.match(browse, /multiple:imageFolderList/);
  assert.match(browse, /setSimulationFolderRoots\(currentTarget, scope, field, selectedPaths\)/);
  assert.match(request, /greenImageRoots:imageRootsForPosition/);
  assert.match(request, /blueImageRoots:imageRootsForPosition/);
  assert.match(field, /data-sim-multiple/);
  assert.match(field, /다중 선택/);
});

test('Keyword mode disables per-position Image Folder and enables multi-folder Keyword Input Root', () => {
  const rows = js.slice(js.indexOf('function simulationPositionRows'), js.indexOf('function simulationPositionToolbar'));
  const options = js.slice(js.indexOf('function greenFilterOptions'), js.indexOf('function greenRuntimeOptions'));
  assert.match(rows, /greenImageRoot[^\n]*'folder','folder',keywordMode/);
  assert.match(rows, /blueImageRoot[^\n]*'folder','folder',keywordMode/);
  assert.match(options, /keywordInputRoot[^\n]*'folder','folder',!obj\.keywordMode/);
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
