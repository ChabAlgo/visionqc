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

test('Agent v0.2.8 version and VPDL 4.2 discovery are consistent', () => {
  assert.match(read('Program.cs'), /AgentVersion = "0\.2\.8"/);
  assert.match(read('Properties/AssemblyInfo.cs'), /AssemblyVersion\("0\.2\.8\.0"\)/);
  assert.match(read('BUILD_RELEASE_x64.cmd'), /v0\.2\.8/);
  assert.match(read('Program.cs'), /VisionPro Deep Learning\\4\.2/);
  assert.match(server, /new\[\] \{ "4\.2", "4\.1", "4\.0", "5\.0" \}/);
  const program = read('Program.cs');
  assert.ok(program.indexOf('Deep Learning\\4.2') < program.indexOf('Deep Learning\\4.1'));
  assert.ok(program.indexOf('Deep Learning\\4.1') < program.indexOf('Deep Learning\\4.0'));
});

test('preloaded Workspace uses the exact object returned by Workspaces.Add', () => {
  assert.match(server, /IWorkspace workspace = control\.Workspaces\.Add\(workspaceName, path\)/);
  assert.match(server, /RuntimeWorkspaceRegistry\.Register\(control, workspaceName, workspace\)/);
  assert.match(models, /ConditionalWeakTable<LocalRuntime\.Control, WorkspaceMap>/);
  assert.match(green, /RuntimeWorkspaceRegistry\.TryGet\(control, workspaceName, out workspace\)/);
  assert.match(blue, /RuntimeWorkspaceRegistry\.TryGet\(control, workspaceName, out workspace\)/);
});

test('preload identity is returned to Web and survives successful runs', () => {
  assert.match(dto, /public string signature \{ get; set; \}/);
  assert.match(server, /runtimePreloadSignature = _preloadedRuntimeSignature/);
  assert.match(server, /response\.signature = _preloadedRuntimeSignature/);
  const run = server.slice(server.indexOf('private void RunSimulation'), server.indexOf('private void OnEngineProgress'));
  assert.match(run, /_preloadedRuntimeControl = simulationControl/);
  assert.match(run, /Simulation 완료 · 사전 로드 Runtime 재사용 가능/);
});

test('picker is owned by a visible STA window on the cursor monitor', () => {
  assert.match(picker, /Application\.Run\(owner\)/);
  assert.match(picker, /Screen\.FromPoint\(Cursor\.Position\)\.WorkingArea/);
  assert.match(picker, /FormBorderStyle\.FixedDialog/);
  assert.match(picker, /Width = width/);
  assert.match(picker, /Height = height/);
  assert.doesNotMatch(picker, /Opacity = 0\.01/);
  assert.equal((picker.match(/dialog\.Show\(ownerHandle\)/g) || []).length, 2);
});

test('stale picker is recoverable only by its owning browser session', () => {
  assert.match(server, /case "\/api\/pick\/cancel"/);
  assert.match(server, /private string _pickerClientId = ""/);
  assert.match(server, /recoverable = !string\.IsNullOrWhiteSpace\(clientId\)/);
  assert.match(server, /다른 브라우저 세션의 선택 창은 취소할 수 없습니다/);
  assert.match(server, /NativeShellPicker\.CancelActiveDialog\(\)/);
  assert.match(picker, /dialog\.Close\(ERROR_CANCELLED_HRESULT\)/);
  assert.equal((picker.match(/SetActiveDialog\(dialog\)/g) || []).length, 2);
  assert.equal((picker.match(/ClearActiveDialog\(dialog\)/g) || []).length, 2);
  assert.equal((server.match(/Monitor\.TryEnter\(_pickerSync\)/g) || []).length, 1);
  assert.equal((server.match(/Monitor\.Exit\(_pickerSync\)/g) || []).length, 1);
});

test('progress logging has no duplicate local boundary or processed INFO', () => {
  const progress = server.slice(server.indexOf('private void OnEngineProgress'), server.indexOf('private void FlushLiveBatch'));
  assert.doesNotMatch(progress, /analysisBoundary/);
  assert.match(progress, /if \(!p\.Processed\.HasValue && p\.LiveRecord == null\) logMessage = p\.Message/);
  assert.match(server, /_lastAgentLogKey/);
});
