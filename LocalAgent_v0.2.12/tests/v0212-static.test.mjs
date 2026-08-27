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

test('Agent v1.3.0 version is consistent', () => {
  assert.match(program, /AgentVersion = "1\.3\.0"/);
  assert.match(read('Properties/AssemblyInfo.cs'), /AssemblyVersion\("1\.3\.0\.0"\)/);
  assert.match(read('BUILD_RELEASE_x64.cmd'), /v1\.3\.0/);
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

test('Integrated Runtime is reusable for a compatible Green-only Simulation', () => {
  assert.match(server, /private bool HasCompatiblePreloadedRuntime/);
  assert.match(server, /requestedMode == "green"/);
  assert.match(server, /_preloadedRuntimeMode, "integrated"/);
  assert.match(server, /BuildRuntimeControlSignature\(req\)/);
  assert.match(server, /BuildGreenWorkspaceSignature\(req\)/);
  assert.match(server, /runtimePreloadControlSignature/);
  assert.match(server, /runtimePreloadGreenWorkspaceSignature/);
  assert.match(read('AgentDtos.cs'), /public string controlSignature/);
  assert.match(read('AgentDtos.cs'), /public string greenWorkspaceSignature/);
});

test('Agent resolves only a healthy VPDL installation matching its managed API', () => {
  const catalog = read('Services/VpdlRuntimeCatalog.cs');
  assert.match(program, /ConfigureVpdlNativeSearchPath\(\)/);
  assert.match(program, /SetDllDirectory\(nativeBin\)/);
  assert.match(program, /GetReferencedAssemblies\(\)/);
  assert.match(program, /ActiveVpdlInstallation/);
  assert.doesNotMatch(program, /VisionPro Deep Learning\\4\.0/);
  assert.match(catalog, /ViDi\.NET\.Local\.dll/);
  assert.match(catalog, /vidi_" \+ apiVersion\.Replace/);
  assert.match(catalog, /File\.Exists\(native\)/);
});

test('VPDL Workers are process-isolated and selected through a Launcher', () => {
  const launcher = read('Launcher/Program.cs');
  const selection = read('Services/VpdlWorkerSelection.cs');
  assert.match(server, /case "\/api\/vpdl\/versions"/);
  assert.match(server, /case "\/api\/vpdl\/select"/);
  assert.match(server, /Program\.RequestWorkerRestart\(\)/);
  assert.match(launcher, /Workers", installation\.ApiVersion, "VisionQC\.VpdlWorker\.exe/);
  assert.match(launcher, /process\.WaitForExit\(\)/);
  assert.match(selection, /RestartExitCode = 74/);
  assert.match(read('BUILD_VPDL_WORKERS.ps1'), /Get-HealthyVpdlInstallations/);
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

test('Keyword mode sends the shared input Root through every enabled Position instead of letting the first Position claim every file', () => {
  const green = read('Engine/GreenOverlayProcessor.cs');
  const start = green.indexOf('private static ImageJobListResult BuildImageJobs');
  const end = green.indexOf('private static List<string> GetInputRoots', start);
  const jobs = green.slice(start, end);
  assert.match(jobs, /FileNameMatchesKeyword\(fileName, slot\.Keyword\)/);
  assert.match(jobs, /string slotImageKey = \(slot\.Key \?\? ""\) \+ "\\n" \+ path/);
  assert.match(jobs, /knownImagePaths\.Add\(slotImageKey\)/);
  assert.ok(jobs.indexOf('FileNameMatchesKeyword') < jobs.indexOf('knownImagePaths.Add(slotImageKey)'));
  assert.match(server, /Keyword = integrated \? "" : FirstNonEmpty\(p\.greenKeyword, p\.keyword\)/);
});

test('installer waits for the installed Agent process before replacing its executable', () => {
  const installer = read('OfflineInstaller/Program.cs');
  assert.match(installer, /StopRunningAgent\(Path\.Combine\(installDir, AgentExe\)\)/);
  assert.match(installer, /IsInstalledAgentRunning\(installedAgentPath\)/);
  assert.match(installer, /Process\.GetProcessesByName/);
});

test('SQLite history and CSV FullPath preview stay in dedicated services', () => {
  assert.match(server, /case "\/api\/image\/preview"/);
  assert.match(server, /case "\/api\/history\/import"/);
  assert.match(server, /new SqliteRunStore/);
  assert.match(read('VisionQC.LocalAgent.csproj'), /System\.Data\.SQLite\.Core/);
  assert.match(read('VisionQC.LocalAgent.csproj'), /Persistence\\SqliteRunStore\.cs/);
  const preview = read('Services/ImagePreviewService.cs');
  assert.match(preview, /MaxSourceBytes/);
  assert.match(preview, /maxDimension/);
  const store = read('Persistence/SqliteRunStore.cs');
  assert.match(store, /PRAGMA journal_mode=WAL/);
  assert.match(store, /CommitBatchSize = 200/);
  assert.match(store, /capture_timestamp/);
  assert.match(store, /overlay_path/);
  const green = read('Engine/GreenOverlayProcessor.cs');
  assert.match(green, /OverlayPath/);
  assert.match(server, /HeatmapImageSave = !integrated/);
});

test('large CSV history import and server-side history search stay outside AgentServer', () => {
  assert.match(server, /case "\/api\/history\/search"/);
  assert.match(server, /case "\/api\/history\/import-file\/start"/);
  assert.match(server, /case "\/api\/history\/import-file\/status"/);
  assert.match(server, /new HistoryService\(_historyStore, _json\)/);
  assert.match(server, /VISIONQC_HISTORY_DB_PATH/);
  assert.match(read('VisionQC.LocalAgent.csproj'), /Services\\HistoryService\.cs/);
  assert.match(read('VisionQC.LocalAgent.csproj'), /Services\\CsvHistoryFileImporter\.cs/);
  const history = read('Services/HistoryService.cs');
  const importer = read('Services/CsvHistoryFileImporter.cs');
  const store = read('Persistence/SqliteRunStore.cs');
  assert.match(history, /Task\.Run\(\(\) => RunFileImport/);
  assert.match(importer, /StreamReader/);
  assert.match(importer, /ReadLine\(\)/);
  assert.match(importer, /CaptureTimestamp/);
  assert.match(store, /AgentHistorySearchResponse Search/);
  assert.match(store, /BuildSearchWhere/);
  assert.match(store, /BuildDeduplicatedHistoryCte/);
  assert.match(store, /UPPER\(IFNULL\(newer\.cell_id,''\)\)=UPPER\(IFNULL\(f\.cell_id,''\)\)/);
  assert.match(store, /UPPER\(IFNULL\(newer\.position_key,''\)\)=UPPER\(IFNULL\(f\.position_key,''\)\)/);
  assert.match(store, /idx_images_capture_result/);
});

test('AI Suggest reuses the loaded Runtime and never writes a single inspection to SQLite', () => {
  assert.match(server, /case "\/api\/classification\/inspect-upload"/);
  assert.match(server, /private object InspectUploadedGreenImage/);
  assert.match(server, /Path\.GetTempPath\(\)/);
  assert.match(server, /PositionResolver\.Resolve/);
  assert.match(server, /_preloadedRuntimeSignature/);
  assert.match(server, /config\.HeatmapImageSave = req\.green != null && req\.green\.heatmapImageSave/);
  assert.doesNotMatch(server, /PersistSingleInspection/);
  assert.doesNotMatch(server, /InspectSingleGreenImageAuto/);
  assert.match(read('AgentDtos.cs'), /bool heatmapImageSave/);
});
