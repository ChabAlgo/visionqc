import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const read = (path) => readFileSync(resolve(root, path), 'utf8');
const server = read('AgentServer.cs');
const picker = read('NativeShellPicker.cs');
const program = read('Program.cs');

test('Agent v0.2.9 version is consistent', () => {
  assert.match(program, /AgentVersion = "0\.2\.9"/);
  assert.match(read('Properties/AssemblyInfo.cs'), /AssemblyVersion\("0\.2\.9\.0"\)/);
  assert.match(read('BUILD_RELEASE_x64.cmd'), /v0\.2\.9/);
});

test('picker HTTP calls return immediately and use idempotent start/status jobs', () => {
  assert.match(server, /case "\/api\/pick\/start"/);
  assert.match(server, /case "\/api\/pick\/status"/);
  const start = server.slice(server.indexOf('private object StartPicker'), server.indexOf('private object PickerStatus'));
  assert.match(start, /RequestId/);
  assert.match(start, /sameRequest/);
  assert.match(start, /Task\.Run\(\(\) => RunPickerJob\(job\)\)/);
  assert.doesNotMatch(start, /\.Wait\(|Thread\.Join/);
  assert.match(server, /private sealed class PickerJob/);
});

test('same-browser stale pickers are recoverable and completed jobs expire', () => {
  assert.match(server, /recoverable = sameClient/);
  assert.match(server, /TimeSpan\.FromSeconds\(30\)/);
  assert.match(server, /_pickerJob\.CancelRequested = true/);
  assert.match(server, /다른 브라우저 세션의 선택 창은 취소할 수 없습니다/);
  assert.match(picker, /PrepareDialogRequest\(\)/);
  assert.match(picker, /_cancelRequested = true/);
  assert.match(picker, /dialog\.Close\(ERROR_CANCELLED_HRESULT\)/);
});

test('remote and virtual initial paths cannot block picker startup', () => {
  assert.match(picker, /FastLocalInitialFolder/);
  assert.match(picker, /GetDriveType\(root\) != DRIVE_FIXED/);
  assert.match(picker, /SetClientGuid/);
  assert.doesNotMatch(picker, /File\.Exists\(initialPath\)/);
  assert.doesNotMatch(picker, /Directory\.Exists\(initialPath\)/);
});

test('Agent returns a concrete JSON error instead of silently closing the socket', () => {
  const handler = server.slice(server.indexOf('private async Task HandleClient'), server.indexOf('private object BuildStatus'));
  assert.match(handler, /catch \(SysException ex\)/);
  assert.match(handler, /WriteJson\(stream, 500/);
  assert.match(handler, /HTTP 요청 처리 실패/);
  assert.match(server, /Internal Server Error/);
});

test('loopback CORS and existing runtime preload behavior stay available', () => {
  assert.match(server, /Access-Control-Allow-Private-Network: true/);
  assert.match(server, /IPAddress\.Loopback/);
  assert.match(server, /runtimePreloadToken = _preloadedRuntimeToken/);
  assert.match(server, /IWorkspace workspace = control\.Workspaces\.Add\(workspaceName, path\)/);
});
