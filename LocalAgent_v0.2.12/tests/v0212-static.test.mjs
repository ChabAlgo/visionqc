import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const read = (path) => readFileSync(resolve(root, path), 'utf8');
const server = read('AgentServer.cs');
const picker = read('NativeShellPicker.cs');
const pickerService = read('Services/PickerService.cs');
const program = read('Program.cs');

test('Agent v1.0.0 version is consistent', () => {
  assert.match(program, /AgentVersion = "1\.0\.0"/);
  assert.match(read('Properties/AssemblyInfo.cs'), /AssemblyVersion\("1\.0\.0\.0"\)/);
  assert.match(read('BUILD_RELEASE_x64.cmd'), /v1\.0\.0/);
});

test('HTTP server delegates picker lifecycle to the isolated picker service', () => {
  assert.match(server, /case "\/api\/pick\/start"/);
  assert.match(server, /case "\/api\/pick\/status"/);
  assert.match(server, /private readonly PickerService _picker/);
  assert.match(server, /_picker\.Start\(DeserializeDictionary\(body\)\)/);
  assert.match(read('VisionQC.LocalAgent.csproj'), /Services\\PickerService\.cs/);
  assert.match(pickerService, /sameRequest/);
  assert.match(pickerService, /TimeSpan\.FromSeconds\(30\)/);
  assert.match(pickerService, /Task\.Run\(\(\) => RunJob\(job\)\)/);
  assert.match(pickerService, /_job\.CancelRequested = true/);
  assert.match(pickerService, /paths = job\.Paths/);
  assert.doesNotMatch(pickerService, /\.Wait\(|Thread\.Join/);
});

test('picker service retains multi-folder selection and browser ownership safeguards', () => {
  assert.match(pickerService, /GetBool\(data, "multiple", false\)/);
  assert.match(pickerService, /AllowMultiple = allowMultiple/);
  assert.match(pickerService, /TryBeginLegacyPicker/);
  assert.match(pickerService, /NativeShellPicker\.CancelActiveDialog/);
  assert.match(picker, /FOS_ALLOWMULTISELECT/);
  assert.match(picker, /internal static string\[\] PickFolders\(string initialPath, bool allowMultiple\)/);
  assert.match(picker, /PreferredInitialFolder/);
  assert.match(picker, /RememberLastSelectedFolder/);
});

test('naming profile parsing is a reusable Agent service with a bounded preview endpoint', () => {
  assert.match(server, /case "\/api\/naming\/preview"/);
  assert.match(server, /request\.fileNames\.Count > 200/);
  assert.match(read('VisionQC.LocalAgent.csproj'), /Services\\NamingProfileParser\.cs/);
  const parser = read('Services/NamingProfileParser.cs');
  assert.match(parser, /DateTime\.TryParseExact\(value, "yyyyMMdd"/);
  assert.match(parser, /DateTime\.TryParseExact\(value, "HHmmss"/);
  assert.match(parser, /token\.All\(char\.IsLetterOrDigit\)/);
  assert.match(read('Services/PositionResolver.cs'), /fileName\.IndexOf\(x\.displayName/);
});

test('single Green inspection is exposed separately and requires reusable runtime', () => {
  assert.match(server, /case "\/api\/classification\/inspect"/);
  assert.match(server, /private object InspectSingleGreenImage/);
  assert.match(server, /PositionResolver\.Resolve/);
  assert.match(server, /_preloadedRuntimeSignature/);
  assert.match(server, /GreenOverlayProcessor\.InspectSingle/);
  assert.match(read('Engine/GreenOverlayProcessor.cs'), /internal static LiveAnalysisRecord InspectSingle/);
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

test('Agent keeps loopback CORS, JSON errors, and multi-root simulation support', () => {
  assert.match(server, /WriteJson\(stream, 500/);
  assert.match(server, /Access-Control-Allow-Private-Network: true/);
  assert.match(server, /IPAddress\.Loopback/);
  assert.match(server, /GetGreenImageRoots\(p\)/);
  assert.match(server, /GetBlueImageRoots\(p\)/);
  assert.match(read('AgentDtos.cs'), /List<string> greenImageRoots/);
  assert.match(read('AgentDtos.cs'), /List<string> blueImageRoots/);
  assert.match(read('Engine/GreenOverlayProcessor.cs'), /InputRoots/);
  assert.match(read('Engine/BlueCropCore.cs'), /ImageRoots/);
});

test('installer waits for the installed Agent process before replacing its executable', () => {
  const installer = read('OfflineInstaller/Program.cs');
  assert.match(installer, /StopRunningAgent\(Path\.Combine\(installDir, AgentExe\)\)/);
  assert.match(installer, /IsInstalledAgentRunning\(installedAgentPath\)/);
  assert.match(installer, /Process\.GetProcessesByName/);
});
