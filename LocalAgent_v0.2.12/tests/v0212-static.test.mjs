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

test('Agent v0.2.12 version is consistent', () => {
  assert.match(program, /AgentVersion = "0\.2\.12"/);
  assert.match(read('Properties/AssemblyInfo.cs'), /AssemblyVersion\("0\.2\.12\.0"\)/);
  assert.match(read('BUILD_RELEASE_x64.cmd'), /v0\.2\.12/);
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
  const folderDialog = picker.slice(picker.indexOf('internal static string PickFolder'), picker.indexOf('internal static string PickFile'));
  const fileDialog = picker.slice(picker.indexOf('internal static string PickFile'), picker.indexOf('private static string ShellItemPath'));
  assert.match(picker, /FastLocalInitialFolder/);
  assert.match(picker, /GetDriveType\(root\) != DRIVE_FIXED/);
  assert.match(picker, /SetClientGuid/);
  assert.match(picker, /ClearClientData\(\)/);
  assert.doesNotMatch(folderDialog, /FOS_HIDEMRUPLACES|FOS_HIDEPINNEDPLACES/);
  assert.doesNotMatch(fileDialog, /FOS_HIDEMRUPLACES|FOS_HIDEPINNEDPLACES/);
  assert.match(picker, /FOS_DONTADDTORECENT/);
  assert.match(picker, /SafeLocalInitialFolder\(\)/);
  assert.doesNotMatch(picker, /File\.Exists\(initialPath\)/);
  assert.doesNotMatch(picker, /Directory\.Exists\(initialPath\)/);
});

test('normal Explorer navigation is restored and automatic cleanup is informational', () => {
  const cancel = server.slice(server.indexOf('private object CancelPicker'), server.indexOf('private object PreviewBlueFallback'));
  assert.match(cancel, /AppendAgentLog\("INFO"/);
  assert.match(cancel, /_pickerJob\.Completed = true/);
  assert.match(cancel, /_pickerJob\.Cancelled = true/);
  assert.match(cancel, /이전 파일\/폴더 선택 작업을 정리했습니다/);
  assert.doesNotMatch(cancel, /응답이 끝나지 않은|AppendAgentLog\(cancelled \? "WARN"/);
});

test('installed VPDL and active Simulation Runtime are separate states', () => {
  const status = server.slice(server.indexOf('private object BuildStatus'), server.indexOf('private object RuntimeCheck'));
  const runtimeCheck = server.slice(server.indexOf('private object RuntimeCheck'), server.indexOf('private RuntimePreloadResponse PreloadRuntime'));
  assert.match(status, /installedVpdlVersion = _vpdlVersion/);
  assert.match(status, /_preloadedRuntimeControl != null \|\| _vpdlReservedForSimulation/);
  assert.match(runtimeCheck, /DisposeInspectionControlLocked\(\)/);
  assert.match(runtimeCheck, /vpdlVersion = "-"/);
  assert.match(server, /new RuntimePreloadResponse \{ ok = true, mode = mode, installedVpdlVersion = _vpdlVersion, vpdlVersion = _vpdlVersion \}/);
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

test('folder picker supports multiple Explorer selections without changing file picker behavior', () => {
  assert.match(picker, /FOS_ALLOWMULTISELECT/);
  assert.match(picker, /interface IFileOpenDialog : IFileDialog/);
  assert.match(picker, /void GetResults\(out IShellItemArray ppenum\)/);
  assert.match(picker, /\[PreserveSig\] new int Show\(IntPtr parent\)/);
  assert.match(picker, /internal static string\[\] PickFolders\(string initialPath, bool allowMultiple\)/);
  assert.match(picker, /dialog\.GetResults\(out resultItems\)/);
  assert.match(picker, /new ManualResetEvent\(false\)/);
  assert.doesNotMatch(picker, /thread\.Join\(\)/);
  assert.match(picker, /ShowInTaskbar = false/);
  assert.match(picker, /TopMost = false/);
});

test('picker jobs and simulation DTO retain every selected image folder', () => {
  assert.match(server, /GetBool\(data, "multiple", false\)/);
  assert.match(server, /AllowMultiple = allowMultiple/);
  assert.match(server, /paths = job\.Paths/);
  assert.match(server, /GetGreenImageRoots\(p\)/);
  assert.match(server, /GetBlueImageRoots\(p\)/);
  assert.match(read('AgentDtos.cs'), /List<string> greenImageRoots/);
  assert.match(read('AgentDtos.cs'), /List<string> blueImageRoots/);
  assert.match(read('Engine/GreenOverlayProcessor.cs'), /InputRoots/);
  assert.match(read('Engine/BlueCropCore.cs'), /ImageRoots/);
  assert.match(read('Engine/BlueCropCore.cs'), /EnumerateSlotImages/);
});
