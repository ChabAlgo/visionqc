import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (name) => fs.readFileSync(path.join(root, name), 'utf8');

test('dashboard threshold controls are contained with native spinner space', () => {
  const css = read('visionqc-extension.css');
  assert.match(css, /vq43-tool-donut-item\{[^}]*overflow:hidden!important/);
  assert.match(css, /grid-template-columns:minmax\(0,1fr\) 48px!important/);
  assert.match(css, /height:30px!important/);
});

test('history Workspace options carry and filter by inspection mode', () => {
  const js = read('visionqc-extension.js');
  const dto = read('LocalAgent_v0.2.12/AgentDtos.cs');
  const store = read('LocalAgent_v0.2.12/Persistence/SqliteRunStore.cs');
  assert.match(js, /function historyWorkspacesForType\(items, workspaceType\)/);
  assert.match(js, /field === 'workspaceType'[\s\S]*historyWorkspaceKey|field === 'workspaceType'[\s\S]*workspaceKey = ''/);
  assert.match(dto, /class AgentHistoryWorkspaceOption[\s\S]*workspaceType/);
  assert.match(store, /workspaceType = type/);
});

test('Simulation precedes Classification and package downloads require confirmation', () => {
  const js = read('visionqc-extension.js');
  assert.ok(js.indexOf("navItem('simulation'") < js.indexOf("navItem('classification'"));
  assert.match(js, /function packageDownloadActionsHtml\(\)/);
  assert.match(js, /window\.confirm\(`\$\{label\}을 다운로드하시겠습니까\?`\)/);
  assert.match(js, /Main Dashboard[\s\S]*packageDownloadActionsHtml\(\)/);
});

test('Simulation controls live in status panel and option boundary scrolls the outer shell', () => {
  const js = read('visionqc-extension.js');
  const status = js.slice(js.indexOf('function simulationStatusPanel'), js.indexOf('function packageDownloadActionsHtml'));
  const render = js.slice(js.indexOf('function renderSimulation()'), js.indexOf('function namingRuleField'));
  assert.match(status, /vq43-sim-status-actions/);
  assert.match(status, /id="vq43-sim-start"/);
  assert.doesNotMatch(render, /vq43-sim-runbar/);
  assert.match(js, /atBottom[\s\S]*outer\.scrollTop \+= event\.deltaY/);
});

test('Runtime Tool cards expose Green Blue and Red color classes', () => {
  const js = read('visionqc-extension.js');
  const css = read('visionqc-extension.css');
  assert.match(js, /typeText\.includes\('red'\)[\s\S]*typeText\.includes\('blue'\)[\s\S]*typeText\.includes\('green'\)/);
  assert.match(css, /vq43-workspace-tool\.green/);
  assert.match(css, /vq43-workspace-tool\.blue/);
  assert.match(css, /vq43-workspace-tool\.red/);
});
