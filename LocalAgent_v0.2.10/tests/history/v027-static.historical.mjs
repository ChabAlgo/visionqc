import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const read = (path) => readFileSync(resolve(root, path), 'utf8');
const server = read('AgentServer.cs');
const dto = read('AgentDtos.cs');
const picker = read('NativeShellPicker.cs');
const models = read('Engine/Models.cs');
const green = read('Engine/GreenOverlayProcessor.cs');
const blue = read('Engine/BlueCropCore.cs');

test('Agent v0.2.7 version is consistent', () => {
  assert.match(read('Program.cs'), /AgentVersion = "0\.2\.7"/);
  assert.match(read('Properties/AssemblyInfo.cs'), /AssemblyVersion\("0\.2\.7\.0"\)/);
  assert.match(read('BUILD_RELEASE_x64.cmd'), /v0\.2\.7/);
});

test('preloaded Workspace uses the exact object returned by Workspaces.Add', () => {
  assert.match(server, /IWorkspace workspace = control\.Workspaces\.Add\(workspaceName, path\)/);
  assert.match(server, /RuntimeWorkspaceRegistry\.Register\(control, workspaceName, workspace\)/);
  assert.match(models, /ConditionalWeakTable<LocalRuntime\.Control, WorkspaceMap>/);
  assert.match(green, /RuntimeWorkspaceRegistry\.TryGet\(control, workspaceName, out workspace\)/);
  assert.match(blue, /RuntimeWorkspaceRegistry\.TryGet\(control, workspaceName, out workspace\)/);
  assert.doesNotMatch(green, /dynamic workspaces = control\.Workspaces/);
  assert.doesNotMatch(blue, /dynamic workspaces = control\.Workspaces/);
});

test('preload identity is returned to Web and survives successful runs', () => {
  assert.match(dto, /public string signature \{ get; set; \}/);
  assert.match(server, /runtimePreloadSignature = _preloadedRuntimeSignature/);
  assert.match(server, /response\.signature = _preloadedRuntimeSignature/);
  const run = server.slice(server.indexOf('private void RunSimulation'), server.indexOf('private void OnEngineProgress'));
  assert.match(run, /_preloadedRuntimeControl = simulationControl/);
  assert.match(run, /Simulation 완료 · 사전 로드 Runtime 재사용 가능/);
});

test('all Control disposal paths remove registry references', () => {
  assert.ok((server.match(/RuntimeWorkspaceRegistry\.Remove\(/g) || []).length >= 4);
  const dispose = server.slice(server.indexOf('private void DisposePreloadedRuntimeLocked'), server.indexOf('private WorkspaceInspectionResponse InspectWorkspace'));
  assert.ok(dispose.indexOf('RuntimeWorkspaceRegistry.Remove') < dispose.indexOf('_preloadedRuntimeControl.Dispose'));
});

test('picker owns a real STA message loop and a visible recovery window', () => {
  assert.match(picker, /Application\.Run\(owner\)/);
  assert.match(picker, /owner\.Shown \+=/);
  assert.match(picker, /Width = 420/);
  assert.match(picker, /Height = 120/);
  assert.doesNotMatch(picker, /Opacity = 0\.01/);
  assert.equal((picker.match(/dialog\.Show\(ownerHandle\)/g) || []).length, 2);
});

test('a second picker request returns busy instead of waiting behind a hidden dialog', () => {
  assert.equal((server.match(/Monitor\.TryEnter\(_pickerSync\)/g) || []).length, 2);
  assert.equal((server.match(/Monitor\.Exit\(_pickerSync\)/g) || []).length, 2);
  assert.match(server, /다른 파일\/폴더 선택 창이 이미 열려 있습니다/);
});

test('progress logging has no dead local boundary or duplicate processed INFO', () => {
  const progress = server.slice(server.indexOf('private void OnEngineProgress'), server.indexOf('private void FlushLiveBatch'));
  assert.doesNotMatch(progress, /analysisBoundary/);
  assert.match(progress, /if \(!p\.Processed\.HasValue && p\.LiveRecord == null\) logMessage = p\.Message/);
  assert.match(server, /_lastAgentLogKey/);
});
